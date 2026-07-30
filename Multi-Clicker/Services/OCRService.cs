using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Tesseract;

namespace MultiClicker.Services
{
    /// <summary>
    /// Service responsible for Optical Character Recognition operations.
    ///
    /// Number recognition strategy ("never trust a single pass"):
    ///  * The captured area is upscaled 4x and then run through several
    ///    preprocessing variants and segmentation modes. Game text can be
    ///    light-on-dark or dark-on-light depending on the UI panel, so both
    ///    polarities are always attempted.
    ///  * Passes are grouped in escalating tiers and stop as soon as a
    ///    trustworthy value appears, so the common well-contrasted case stays
    ///    fast. The later tiers exist for faint / greyed-out text (an inactive
    ///    sell quantity is drawn only a few grey levels away from the panel
    ///    background): contrast is stretched to the full range, binarized with
    ///    a bias that keeps anti-aliased strokes instead of eroding them, then
    ///    also thresholded locally (adaptive mean) and stroke-thickened.
    ///  * Every pass is sanitized down to digits only (thousand separators,
    ///    currency suffixes and stray glyphs are discarded) and validated
    ///    against an optional whitelist of allowed values.
    ///  * A high-confidence early hit returns immediately; otherwise the
    ///    candidates vote weighted by confidence, so one solid read beats
    ///    several weak ones agreeing. A pass Tesseract reports no confidence in
    ///    has invented digits out of noise and never votes at all. A call
    ///    constrained by a whitelist gets one last-resort relaxed bar, since
    ///    only a handful of values are legal there. When nothing trustworthy is
    ///    found the caller gets -1 - never a garbage number.
    /// </summary>
    public static class OCRService
    {
        #region Private Fields
        private static readonly object EngineLock = new object();
        private static TesseractEngine _engine;
        private static readonly string OcrLanguage = "fra";

        // Digits plus the separators Dofus uses in prices; everything else is
        // rejected at the Tesseract level so separator glyphs are never forced
        // into a bogus digit.
        private const string NumberWhitelist = "0123456789 .,";

        private const float EarlyExitConfidence = 0.85f;
        private const float MinimumSingleConfidence = 0.45f;

        // Candidates are weighted by confidence, not counted: a pass reporting
        // zero confidence has not read anything, it has invented digits out of
        // noise, and two such passes agreeing must never outvote one good read.
        private const float MinimumAgreementScore = 0.50f;

        // Whitelisted reads get a final relaxed bar: only a handful of values are
        // legal, so a faint greyed-out glyph is worth taking rather than aborting.
        private const float LastResortScore = 0.35f;

        private const int MaxDigits = 9;
        private const int UpscaleFactor = 4;

        // Extra grey levels claimed for the glyph when binarizing faint text, so
        // anti-aliased strokes survive the threshold instead of being eroded.
        private const int FaintTextBias = 12;

        private static string ResolveTessdataPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "tessdata"),
                Path.Combine(baseDir, "mandatory_assets", "tessdata"),
                Path.Combine(Directory.GetCurrentDirectory(), "tessdata")
            };

            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate) &&
                    File.Exists(Path.Combine(candidate, OcrLanguage + ".traineddata")))
                {
                    return candidate;
                }
            }

            Trace.WriteLine("tessdata directory not found. Looked in: " + string.Join("; ", candidates));
            return null;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets a value indicating whether the OCR engine is initialized
        /// </summary>
        public static bool IsInitialized => _engine != null;
        #endregion

        #region Public Methods
        /// <summary>
        /// Initializes the OCR engine
        /// </summary>
        public static void InitializeEngine()
        {
            lock (EngineLock)
            {
                try
                {
                    if (_engine == null)
                    {
                        var tessdataPath = ResolveTessdataPath();
                        if (tessdataPath == null)
                        {
                            return;
                        }

                        _engine = new TesseractEngine(tessdataPath, OcrLanguage, EngineMode.Default);
                        Trace.WriteLine($"OCR Engine initialized successfully (tessdata: {tessdataPath})");
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Failed to initialize OCR engine: {ex.Message}");
                    _engine = null;
                }
            }
        }

        /// <summary>
        /// Recognizes a number displayed in the given absolute screen rectangle.
        /// Captures the screen up to two times (transient overlays/tooltips can
        /// ruin a single frame) and runs the multi-variant pipeline on each.
        /// </summary>
        /// <param name="screenRect">Absolute screen rectangle to read.</param>
        /// <param name="allowedValues">Optional whitelist; any value outside it is rejected.</param>
        /// <returns>The recognized number, or -1 when no trustworthy value was found.</returns>
        public static int RecognizeNumberOnScreen(Rectangle screenRect, int[] allowedValues = null)
        {
            if (screenRect.Width < 3 || screenRect.Height < 3)
            {
                Trace.WriteLine($"OCR skipped: capture rectangle {screenRect} is not configured.");
                return -1;
            }

            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using (var capture = CaptureScreenArea(screenRect))
                    {
                        var value = RecognizeNumberFromBitmap(capture, allowedValues);
                        if (value >= 0) return value;
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"OCR capture attempt {attempt + 1} failed: {ex.Message}");
                }

                Thread.Sleep(120);
            }

            return -1;
        }

        /// <summary>
        /// Runs the multi-variant recognition pipeline on an already-captured
        /// image. Internal so it can be exercised directly by test harnesses.
        /// </summary>
        internal static int RecognizeNumberFromBitmap(Bitmap source, int[] allowedValues = null)
        {
            if (!IsInitialized) InitializeEngine();
            if (!IsInitialized)
            {
                Trace.WriteLine("OCR engine not available");
                return -1;
            }

            lock (EngineLock)
            {
                try
                {
                    _engine.SetVariable("tessedit_char_whitelist", NumberWhitelist);

                    using (var upscaled = UpscaleForOcr(source, UpscaleFactor))
                    {
                        var gray = ToGrayBuffer(upscaled, out int width, out int height);
                        int otsu = OtsuThreshold(gray);
                        int legacy = LegacyThreshold(gray);

                        // The majority class is the background, so its polarity
                        // tells us whether glyphs are lighter or darker than it.
                        // Faint variants try that polarity first.
                        bool lightText = IsLightTextOnDarkBackground(gray, otsu);

                        // Buffers are built on demand and cached: the escalating
                        // tiers reuse the same buffer under a different
                        // segmentation mode, and the thickened variants reuse the
                        // plain binarization they are derived from.
                        var cache = new Dictionary<string, byte[]>();
                        byte[] Cached(string name, Func<byte[]> build)
                        {
                            if (!cache.TryGetValue(name, out var buffer))
                            {
                                buffer = build();
                                cache[name] = buffer;
                            }
                            return buffer;
                        }

                        // Denoise before stretching: the stretch multiplies the gap
                        // between glyph and background, and would multiply screen
                        // noise with it.
                        byte[] Stretched() => Cached("stretched",
                            () => StretchContrast(MedianFilter3(gray, width, height)));

                        int stretchedOtsu = -1;
                        int StretchedOtsu()
                        {
                            if (stretchedOtsu < 0) stretchedOtsu = OtsuThreshold(Stretched());
                            return stretchedOtsu;
                        }

                        byte[] Adaptive(bool light) => Cached(
                            "adaptive" + PolaritySuffix(light),
                            () => AdaptiveThreshold(Stretched(), width, height, light, FaintTextBias));

                        // Ordered from usually-best to fallback. Tesseract does its
                        // own internal thresholding, so plain grayscale often wins;
                        // explicit binarizations rescue low-contrast cases.
                        var normalVariants = new (string name, Func<byte[]> build)[]
                        {
                            ("gray", () => gray),
                            ("otsu", () => Cached("otsu", () => MapBuffer(gray, g => g > otsu ? (byte)255 : (byte)0))),
                            ("otsu-inverted", () => Cached("otsu-inverted", () => MapBuffer(gray, g => g > otsu ? (byte)0 : (byte)255))),
                            ("legacy-threshold", () => Cached("legacy", () => MapBuffer(gray, g => g > legacy ? (byte)255 : (byte)0))),
                        };

                        // Faint / greyed-out text rescue. All of these normalize to
                        // dark glyphs on white, which is what Tesseract expects.
                        (string name, Func<byte[]> build)[] FaintVariants(bool light) => new (string, Func<byte[]>)[]
                        {
                            ("stretched", Stretched),
                            ("stretched-binary" + PolaritySuffix(light),
                                () => Cached("stretched-binary" + PolaritySuffix(light),
                                    () => Binarize(Stretched(), StretchedOtsu(), light, FaintTextBias))),
                            ("adaptive" + PolaritySuffix(light), () => Adaptive(light)),
                            ("adaptive-thick" + PolaritySuffix(light),
                                () => Cached("adaptive-thick" + PolaritySuffix(light),
                                    () => ThickenDarkStrokes(Adaptive(light), width, height))),
                        };

                        // The secondary polarity drops the grayscale variant: it is
                        // polarity-agnostic and already covered above.
                        var secondaryFaint = FaintVariants(!lightText).Skip(1).ToArray();

                        var tiers = new (string label, (string name, Func<byte[]> build)[] variants, PageSegMode mode)[]
                        {
                            ("normal/line", normalVariants, PageSegMode.SingleLine),
                            ("normal/word", normalVariants, PageSegMode.SingleWord),
                            ("faint/line", FaintVariants(lightText), PageSegMode.SingleLine),
                            ("faint-alt/line", secondaryFaint, PageSegMode.SingleLine),
                            ("faint/word", FaintVariants(lightText).Skip(1).ToArray(), PageSegMode.SingleWord),
                        };

                        var candidates = new List<(int value, float confidence, string variant)>();

                        foreach (var (label, variants, mode) in tiers)
                        {
                            foreach (var (name, build) in variants)
                            {
                                var candidate = RunNumberOcr(build(), width, height, mode, $"{name}/{mode}");
                                if (candidate == null) continue;

                                var (value, confidence) = candidate.Value;
                                if (allowedValues != null && !allowedValues.Contains(value)) continue;
                                if (value <= 0) continue;
                                if (confidence <= 0f) continue;

                                candidates.Add((value, confidence, name));
                                if (confidence >= EarlyExitConfidence)
                                {
                                    Trace.WriteLine($"OCR accepted {value} ({name}/{mode}, confidence {confidence:F2})");
                                    return value;
                                }
                            }

                            // Consensus: the best-supported value wins, either
                            // through one confident read or through several
                            // agreeing ones. Checked after every tier so a clean
                            // read never pays for the faint-text tiers, but always
                            // under the strict rule - the relaxed whitelist bar
                            // only applies once every tier has had its say.
                            var settled = Consensus(candidates);
                            if (settled != null &&
                                (settled.Value.best >= MinimumSingleConfidence ||
                                 (settled.Value.count >= 2 && settled.Value.score >= MinimumAgreementScore)))
                            {
                                Trace.WriteLine($"OCR consensus {settled.Value.value} after tier {label} " +
                                                $"({settled.Value.count} passes, score {settled.Value.score:F2}, " +
                                                $"best confidence {settled.Value.best:F2})");
                                return settled.Value.value;
                            }
                        }

                        var best = Consensus(candidates);
                        if (best == null)
                        {
                            Trace.WriteLine("OCR found no plausible number in any variant.");
                            return -1;
                        }

                        // Last resort for a whitelisted read: only a handful of values
                        // are legal, so a faint greyed-out glyph beats aborting. Only
                        // reached once every tier has had its say, so a stronger or
                        // better-supported read always wins first.
                        if (allowedValues != null && best.Value.score >= LastResortScore)
                        {
                            Trace.WriteLine($"OCR accepted whitelisted low-confidence {best.Value.value} " +
                                            $"({best.Value.count} passes, score {best.Value.score:F2})");
                            return best.Value.value;
                        }

                        Trace.WriteLine($"OCR rejected weakly-supported candidate {best.Value.value} " +
                                        $"({best.Value.count} passes, score {best.Value.score:F2}, " +
                                        $"best confidence {best.Value.best:F2}).");
                        return -1;
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"OCR number recognition failed: {ex.Message}");
                    return -1;
                }
                finally
                {
                    try { _engine.SetVariable("tessedit_char_whitelist", ""); } catch { }
                }
            }
        }

        /// <summary>
        /// Reduces raw OCR output to a clean integer: keeps digits only (drops
        /// thousand separators, currency suffixes, stray glyphs), trims leading
        /// zeros, and rejects empty or absurdly long results.
        /// Returns -1 when the text contains no usable number.
        /// </summary>
        internal static int SanitizeRecognizedNumber(string rawText)
        {
            if (string.IsNullOrEmpty(rawText)) return -1;

            var digits = new System.Text.StringBuilder(rawText.Length);
            foreach (var ch in rawText)
            {
                if (ch >= '0' && ch <= '9') digits.Append(ch);
            }

            if (digits.Length == 0) return -1;

            var trimmed = digits.ToString().TrimStart('0');
            if (trimmed.Length == 0) return 0;
            if (trimmed.Length > MaxDigits) return -1;

            return int.Parse(trimmed);
        }

        /// <summary>
        /// Disposes the OCR engine resources
        /// </summary>
        public static void Dispose()
        {
            lock (EngineLock)
            {
                _engine?.Dispose();
                _engine = null;
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Picks the winning candidate by summed confidence, so several weak
        /// reads cannot outweigh one strong one while genuine agreement still
        /// adds up. Agreement between two segmentation modes of the same variant
        /// counts - on faint text that is often the only agreement there is.
        /// Null when there is nothing to pick.
        /// </summary>
        private static (int value, int count, float score, float best)? Consensus(
            List<(int value, float confidence, string variant)> candidates)
        {
            if (candidates.Count == 0) return null;

            var winner = candidates
                .GroupBy(c => c.value)
                .Select(g => new
                {
                    Value = g.Key,
                    Count = g.Count(),
                    Score = g.Sum(c => c.confidence),
                    Best = g.Max(c => c.confidence)
                })
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Best)
                .First();

            return (winner.Value, winner.Count, (float)winner.Score, winner.Best);
        }

        /// <summary>
        /// Runs one OCR pass over an already-preprocessed gray buffer.
        /// Returns the sanitized value with its confidence, or null.
        /// </summary>
        private static (int value, float confidence)? RunNumberOcr(
            byte[] buffer, int width, int height, PageSegMode segMode, string label)
        {
            try
            {
                using (var bmp = BuildBitmapFromGray(buffer, width, height))
                using (var pix = PixConverter.ToPix(bmp))
                using (var page = _engine.Process(pix, segMode))
                {
                    var raw = page.GetText();
                    var confidence = page.GetMeanConfidence();
                    var value = SanitizeRecognizedNumber(raw);

                    Trace.WriteLine($"OCR [{label}] raw='{(raw ?? "").Trim()}' -> {value} (confidence {confidence:F2})");
                    return value >= 0 ? (value, confidence) : ((int, float)?)null;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"OCR variant {label} failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Captures an absolute screen rectangle. Positions are stored as raw
        /// screen coordinates by the position picker, so no offset is applied.
        /// </summary>
        private static Bitmap CaptureScreenArea(Rectangle screenRectangle)
        {
            var bitmap = new Bitmap(screenRectangle.Width, screenRectangle.Height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(screenRectangle.Left, screenRectangle.Top, 0, 0, bitmap.Size);
            }
            return bitmap;
        }

        private static Bitmap UpscaleForOcr(Bitmap src, int scale)
        {
            var upscaled = new Bitmap(Math.Max(1, src.Width * scale), Math.Max(1, src.Height * scale), PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(upscaled))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, upscaled.Width, upscaled.Height));
            }
            return upscaled;
        }

        private static byte[] ToGrayBuffer(Bitmap bmp, out int width, out int height)
        {
            width = bmp.Width;
            height = bmp.Height;
            var gray = new byte[width * height];

            var data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                int stride = data.Stride;
                var row = new byte[stride];
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(data.Scan0 + y * stride, row, 0, stride);
                    for (int x = 0; x < width; x++)
                    {
                        int idx = x * 3;
                        gray[y * width + x] = (byte)(0.299 * row[idx + 2] + 0.587 * row[idx + 1] + 0.114 * row[idx]);
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            return gray;
        }

        private static byte[] MapBuffer(byte[] gray, Func<byte, byte> map)
        {
            var result = new byte[gray.Length];
            for (int i = 0; i < gray.Length; i++) result[i] = map(gray[i]);
            return result;
        }

        private static string PolaritySuffix(bool lightText) => lightText ? "-light" : "-dark";

        /// <summary>
        /// The background is the majority class, so comparing populations either
        /// side of the global threshold tells us the text polarity.
        /// </summary>
        private static bool IsLightTextOnDarkBackground(byte[] gray, int threshold)
        {
            int bright = 0;
            foreach (var g in gray)
            {
                if (g > threshold) bright++;
            }
            return bright * 2 < gray.Length;
        }

        /// <summary>
        /// 3x3 median filter. Cheap screen-capture denoise that keeps stroke
        /// edges (unlike a blur), so the contrast stretch that follows amplifies
        /// the glyph instead of the panel's dithering.
        /// </summary>
        private static byte[] MedianFilter3(byte[] gray, int width, int height)
        {
            if (width < 3 || height < 3) return gray;

            var result = new byte[gray.Length];
            var window = new byte[9];
            for (int y = 0; y < height; y++)
            {
                int yStart = Math.Max(0, y - 1);
                int yEnd = Math.Min(height - 1, y + 1);
                for (int x = 0; x < width; x++)
                {
                    int xStart = Math.Max(0, x - 1);
                    int xEnd = Math.Min(width - 1, x + 1);

                    int count = 0;
                    for (int ny = yStart; ny <= yEnd; ny++)
                    {
                        for (int nx = xStart; nx <= xEnd; nx++)
                        {
                            window[count++] = gray[ny * width + nx];
                        }
                    }

                    Array.Sort(window, 0, count);
                    result[y * width + x] = window[count / 2];
                }
            }
            return result;
        }

        /// <summary>
        /// Rescales the used part of the histogram onto the full 0-255 range.
        /// This is what makes greyed-out text readable: an inactive glyph sits
        /// only a few levels away from the panel background, which every
        /// downstream threshold (Tesseract's included) then struggles to split.
        /// Trims 0.2% at each end so a stray hot/dead pixel cannot flatten the
        /// stretch, and gives up when the crop is genuinely uniform rather than
        /// amplifying pure noise into glyph-shaped garbage.
        /// </summary>
        private static byte[] StretchContrast(byte[] gray)
        {
            var histogram = new int[256];
            foreach (var g in gray) histogram[g]++;

            int trim = Math.Max(1, gray.Length / 500);

            int low = 0, high = 255;
            for (int i = 0, seen = 0; i < 256; i++)
            {
                seen += histogram[i];
                if (seen > trim) { low = i; break; }
            }
            for (int i = 255, seen = 0; i >= 0; i--)
            {
                seen += histogram[i];
                if (seen > trim) { high = i; break; }
            }

            int range = high - low;
            if (range < 6)
            {
                Trace.WriteLine($"OCR contrast stretch skipped: flat crop (levels {low}-{high}).");
                return gray;
            }

            var lookup = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                int scaled = (i - low) * 255 / range;
                lookup[i] = (byte)Math.Max(0, Math.Min(255, scaled));
            }

            var result = new byte[gray.Length];
            for (int i = 0; i < gray.Length; i++) result[i] = lookup[gray[i]];
            return result;
        }

        /// <summary>
        /// Global binarization normalized to dark glyphs on white. The bias
        /// widens the foreground class by a few grey levels so the anti-aliased
        /// edge of a faint stroke is kept instead of being eroded away.
        /// </summary>
        private static byte[] Binarize(byte[] gray, int threshold, bool lightText, int bias)
        {
            int cut = lightText
                ? Math.Max(0, threshold - bias)
                : Math.Min(255, threshold + bias);

            var result = new byte[gray.Length];
            for (int i = 0; i < gray.Length; i++)
            {
                bool foreground = lightText ? gray[i] > cut : gray[i] < cut;
                result[i] = foreground ? (byte)0 : (byte)255;
            }
            return result;
        }

        /// <summary>
        /// Local mean thresholding over an integral image, normalized to dark
        /// glyphs on white. Unlike Otsu this survives a background gradient and
        /// a foreground that only barely differs from it - the exact shape of a
        /// greyed-out UI label.
        /// </summary>
        private static byte[] AdaptiveThreshold(byte[] gray, int width, int height, bool lightText, int offset)
        {
            int stride = width + 1;
            var integral = new long[stride * (height + 1)];
            for (int y = 0; y < height; y++)
            {
                long rowSum = 0;
                for (int x = 0; x < width; x++)
                {
                    rowSum += gray[y * width + x];
                    integral[(y + 1) * stride + (x + 1)] = integral[y * stride + (x + 1)] + rowSum;
                }
            }

            // Window roughly the glyph height: big enough to hold background
            // around a stroke, small enough to track local shading.
            int half = Math.Max(3, Math.Min(width, height) / 3);

            var result = new byte[gray.Length];
            for (int y = 0; y < height; y++)
            {
                int y0 = Math.Max(0, y - half);
                int y1 = Math.Min(height - 1, y + half);
                for (int x = 0; x < width; x++)
                {
                    int x0 = Math.Max(0, x - half);
                    int x1 = Math.Min(width - 1, x + half);

                    long sum = integral[(y1 + 1) * stride + (x1 + 1)]
                             - integral[y0 * stride + (x1 + 1)]
                             - integral[(y1 + 1) * stride + x0]
                             + integral[y0 * stride + x0];
                    int count = (y1 - y0 + 1) * (x1 - x0 + 1);
                    int mean = (int)(sum / count);

                    byte g = gray[y * width + x];
                    bool foreground = lightText ? g > mean + offset : g < mean - offset;
                    result[y * width + x] = foreground ? (byte)0 : (byte)255;
                }
            }
            return result;
        }

        /// <summary>
        /// 3x3 minimum filter: grows the dark strokes of a dark-on-white binary
        /// image. A faint glyph loses most of its stroke width to thresholding,
        /// and Tesseract reads a fattened digit far better than a broken one.
        /// </summary>
        private static byte[] ThickenDarkStrokes(byte[] binary, int width, int height)
        {
            var result = new byte[binary.Length];
            for (int y = 0; y < height; y++)
            {
                int yStart = Math.Max(0, y - 1);
                int yEnd = Math.Min(height - 1, y + 1);
                for (int x = 0; x < width; x++)
                {
                    int xStart = Math.Max(0, x - 1);
                    int xEnd = Math.Min(width - 1, x + 1);

                    byte min = 255;
                    for (int ny = yStart; ny <= yEnd; ny++)
                    {
                        for (int nx = xStart; nx <= xEnd; nx++)
                        {
                            var v = binary[ny * width + nx];
                            if (v < min) min = v;
                        }
                    }
                    result[y * width + x] = min;
                }
            }
            return result;
        }

        /// <summary>
        /// Materializes a gray buffer as a bitmap for Tesseract.
        /// No quiet margin is added on purpose: padding the crop measurably
        /// broke the faintest light-on-dark reads (a uniform border throws off
        /// Tesseract's own inversion/layout detection on these tiny images).
        /// </summary>
        private static Bitmap BuildBitmapFromGray(byte[] gray, int width, int height)
        {
            var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            var data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                int stride = data.Stride;
                var row = new byte[stride];
                for (int y = 0; y < height; y++)
                {
                    int sourceRow = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        var v = gray[sourceRow + x];
                        int idx = x * 3;
                        row[idx] = v;
                        row[idx + 1] = v;
                        row[idx + 2] = v;
                    }
                    Marshal.Copy(row, 0, data.Scan0 + y * stride, stride);
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            return bmp;
        }

        /// <summary>
        /// Otsu's method: picks the threshold that maximizes between-class
        /// variance - adapts to whatever contrast the game panel provides.
        /// </summary>
        private static int OtsuThreshold(byte[] gray)
        {
            var histogram = new int[256];
            foreach (var g in gray) histogram[g]++;

            long total = gray.Length;
            long sumAll = 0;
            for (int i = 0; i < 256; i++) sumAll += (long)i * histogram[i];

            long sumBackground = 0;
            long weightBackground = 0;
            double maxVariance = -1;
            int threshold = 127;

            for (int t = 0; t < 256; t++)
            {
                weightBackground += histogram[t];
                if (weightBackground == 0) continue;
                long weightForeground = total - weightBackground;
                if (weightForeground == 0) break;

                sumBackground += (long)t * histogram[t];
                double meanBackground = (double)sumBackground / weightBackground;
                double meanForeground = (double)(sumAll - sumBackground) / weightForeground;
                double variance = (double)weightBackground * weightForeground *
                                  (meanBackground - meanForeground) * (meanBackground - meanForeground);

                if (variance > maxVariance)
                {
                    maxVariance = variance;
                    threshold = t;
                }
            }

            return threshold;
        }

        /// <summary>
        /// The historical fixed-ratio threshold, kept as one voting variant.
        /// </summary>
        private static int LegacyThreshold(byte[] gray)
        {
            int min = 255, max = 0;
            foreach (var g in gray)
            {
                if (g < min) min = g;
                if (g > max) max = g;
            }
            int range = max - min;
            return range < 20 ? (min + max) / 2 : min + (int)(range * 0.4);
        }
        #endregion
    }
}
