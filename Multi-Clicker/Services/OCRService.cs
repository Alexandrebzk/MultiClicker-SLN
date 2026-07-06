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
    ///  * The captured area is upscaled 4x, then run through several
    ///    preprocessing variants (raw grayscale, Otsu binarization, inverted
    ///    Otsu, legacy fixed-ratio threshold) and two Tesseract segmentation
    ///    modes. Game text can be light-on-dark or dark-on-light depending on
    ///    the UI panel, so both polarities are always attempted.
    ///  * Every pass is sanitized down to digits only (thousand separators,
    ///    currency suffixes and stray glyphs are discarded) and validated
    ///    against an optional whitelist of allowed values.
    ///  * A high-confidence early hit returns immediately; otherwise the
    ///    candidates vote and a value is accepted only when at least two
    ///    variants agree or one variant is reasonably confident. When nothing
    ///    trustworthy is found the caller gets -1 - never a garbage number.
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
        private const int MaxDigits = 9;

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

                    using (var upscaled = UpscaleForOcr(source, 4))
                    {
                        var gray = ToGrayBuffer(upscaled, out int width, out int height);
                        int otsu = OtsuThreshold(gray);
                        int legacy = LegacyThreshold(gray);

                        // Ordered from usually-best to fallback. Tesseract does its
                        // own internal thresholding, so plain grayscale often wins;
                        // explicit binarizations rescue low-contrast cases.
                        var variants = new (string name, Func<byte, byte> map)[]
                        {
                            ("gray", g => g),
                            ("otsu", g => g > otsu ? (byte)255 : (byte)0),
                            ("otsu-inverted", g => g > otsu ? (byte)0 : (byte)255),
                            ("legacy-threshold", g => g > legacy ? (byte)255 : (byte)0),
                        };
                        var segModes = new[] { PageSegMode.SingleLine, PageSegMode.SingleWord };

                        var candidates = new List<(int value, float confidence, string variant)>();

                        foreach (var mode in segModes)
                        {
                            foreach (var (name, map) in variants)
                            {
                                var candidate = RunNumberOcr(gray, width, height, map, mode, $"{name}/{mode}");
                                if (candidate == null) continue;

                                var (value, confidence) = candidate.Value;
                                if (allowedValues != null && !allowedValues.Contains(value)) continue;
                                if (value <= 0) continue;

                                candidates.Add((value, confidence, name));
                                if (confidence >= EarlyExitConfidence)
                                {
                                    Trace.WriteLine($"OCR accepted {value} ({name}/{mode}, confidence {confidence:F2})");
                                    return value;
                                }
                            }
                        }

                        if (candidates.Count == 0)
                        {
                            Trace.WriteLine("OCR found no plausible number in any variant.");
                            return -1;
                        }

                        // Consensus: the value seen by the most variants wins; a
                        // lone candidate must be reasonably confident.
                        var best = candidates
                            .GroupBy(c => c.value)
                            .Select(g => new { Value = g.Key, Count = g.Count(), Confidence = g.Max(c => c.confidence) })
                            .OrderByDescending(x => x.Count)
                            .ThenByDescending(x => x.Confidence)
                            .First();

                        if (best.Count >= 2 || best.Confidence >= MinimumSingleConfidence)
                        {
                            Trace.WriteLine($"OCR consensus {best.Value} ({best.Count} variants, max confidence {best.Confidence:F2})");
                            return best.Value;
                        }

                        Trace.WriteLine($"OCR rejected lone low-confidence candidate {best.Value} (confidence {best.Confidence:F2}).");
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
        /// Runs one OCR pass over the gray buffer transformed by the given map.
        /// Returns the sanitized value with its confidence, or null.
        /// </summary>
        private static (int value, float confidence)? RunNumberOcr(
            byte[] gray, int width, int height, Func<byte, byte> map, PageSegMode segMode, string label)
        {
            try
            {
                using (var bmp = BuildBitmapFromGray(gray, width, height, map))
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

        private static Bitmap BuildBitmapFromGray(byte[] gray, int width, int height, Func<byte, byte> map)
        {
            var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            var data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                int stride = data.Stride;
                var row = new byte[stride];
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        var v = map(gray[y * width + x]);
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
