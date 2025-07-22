using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;
using Tesseract;

namespace MultiClicker.Services
{
    /// <summary>
    /// Service responsible for Optical Character Recognition operations
    /// </summary>
    public static class OCRService
    {
        #region Private Fields
        private static readonly object EngineLock = new object();
        private static TesseractEngine _engine;
        private static readonly string OcrLanguage = "fra";
        private static readonly string TessdataPath = @"tessdata";
        private static readonly int BinarizationThreshold = 140;
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
                        _engine = new TesseractEngine(TessdataPath, OcrLanguage, EngineMode.Default);
                        Trace.WriteLine("OCR Engine initialized successfully");
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
        /// Performs OCR on the specified image
        /// </summary>
        /// <param name="image">The image to process</param>
        /// <returns>The recognized text</returns>
        public static string RecognizeText(Image image)
        {
            if (!IsInitialized)
            {
                InitializeEngine();
            }

            if (!IsInitialized)
            {
                Trace.WriteLine("OCR engine not available");
                return string.Empty;
            }

            lock (EngineLock)
            {
                try
                {
                    using (var processedImage = PreprocessImage(image))
                    using (var page = _engine.Process(processedImage))
                    {
                        var result = page.GetText();
                        return result?.Trim() ?? string.Empty;
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"OCR processing failed: {ex.Message}");
                    return string.Empty;
                }
            }
        }

        /// <summary>
        /// Performs OCR on a specific region of the image
        /// </summary>
        /// <param name="image">The source image</param>
        /// <param name="region">The region to process</param>
        /// <returns>The recognized text</returns>
        public static string RecognizeTextInRegion(Image image, Rectangle region)
        {
            try
            {
                using (var croppedImage = CropImage(image, region))
                {
                    return RecognizeText(croppedImage);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Failed to process region: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Performs HDV (Hotel de Vente) OCR processing with automatic amount filling
        /// </summary>
        /// <param name="AmountToFill">The amount to fill in the interface</param>
        public static void ProcessHDVOCR(int AmountToFill)
        {
            Trace.WriteLine("-------------------------");
            Trace.WriteLine("Starting HDV OCR processing...");
            try
            {
                // Take screenshot and process OCR
                using (var screenshot = CaptureScreen())
                using (var processedImage = PreprocessForOCR(screenshot))
                {
                    var recognizedText = RecognizeText(processedImage);
                    Trace.WriteLine($"OCR Result: {recognizedText}");
                    
                    // Simulate UI interaction for amount filling
                    Thread.Sleep(50);
                    SendKeys.SendWait("{DELETE}");
                    SendKeys.SendWait((AmountToFill - 1).ToString().Trim());
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"HDV OCR processing failed: {ex.Message}, Trace: {ex.StackTrace}");
            }
            Trace.WriteLine("-------------------------");
        }

        /// <summary>
        /// Captures the current screen
        /// </summary>
        /// <returns>Screenshot bitmap</returns>
        public static Bitmap CaptureScreen()
        {
            var bounds = Screen.PrimaryScreen.Bounds;
            var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);
            }
            return bitmap;
        }

        /// <summary>
        /// Advanced preprocessing for OCR with upscaling, contrast enhancement, and binarization
        /// </summary>
        /// <param name="src">Source bitmap</param>
        /// <returns>Processed bitmap optimized for OCR</returns>
        public static Bitmap PreprocessForOCR(Bitmap src)
        {
            // Upscale ×2
            Bitmap upscaled = new Bitmap(src.Width * 2, src.Height * 2);
            using (Graphics g = Graphics.FromImage(upscaled))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, new Rectangle(0, 0, upscaled.Width, upscaled.Height));
            }

            // Enhance contrast
            float contrast = 1.5f; // Contrast factor
            float t = (1.0f - contrast) / 2.0f;

            ColorMatrix colorMatrix = new ColorMatrix(new float[][]
            {
                new float[] {contrast, 0, 0, 0, 0},
                new float[] {0, contrast, 0, 0, 0},
                new float[] {0, 0, contrast, 0, 0},
                new float[] {0, 0, 0, 1, 0},
                new float[] {t, t, t, 0, 1}
            });

            using (ImageAttributes imageAttributes = new ImageAttributes())
            {
                imageAttributes.SetColorMatrix(colorMatrix);
                
                Bitmap contrastEnhanced = new Bitmap(upscaled.Width, upscaled.Height);
                using (Graphics g = Graphics.FromImage(contrastEnhanced))
                {
                    g.DrawImage(upscaled, new Rectangle(0, 0, upscaled.Width, upscaled.Height),
                        0, 0, upscaled.Width, upscaled.Height, GraphicsUnit.Pixel, imageAttributes);
                }
                
                upscaled.Dispose();
                
                // Apply binarization
                BinarizeImage(contrastEnhanced);
                
                return contrastEnhanced;
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Preprocesses the image for better OCR results
        /// </summary>
        /// <param name="source">The source image</param>
        /// <returns>The preprocessed image</returns>
        private static Bitmap PreprocessImage(Image source)
        {
            var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.DrawImage(source, 0, 0);
            }

            // Apply binarization for better OCR results
            BinarizeImage(bitmap);
            
            return bitmap;
        }

        /// <summary>
        /// Applies binarization to the image
        /// </summary>
        /// <param name="bitmap">The bitmap to process</param>
        private static void BinarizeImage(Bitmap bitmap)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                for (int y = 0; y < bitmap.Height; y++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    var grayValue = (int)(pixel.R * 0.299 + pixel.G * 0.587 + pixel.B * 0.114);
                    var newColor = grayValue > BinarizationThreshold ? Color.White : Color.Black;
                    bitmap.SetPixel(x, y, newColor);
                }
            }
        }

        /// <summary>
        /// Crops the image to the specified region
        /// </summary>
        /// <param name="source">The source image</param>
        /// <param name="region">The region to crop</param>
        /// <returns>The cropped image</returns>
        private static Bitmap CropImage(Image source, Rectangle region)
        {
            var croppedBitmap = new Bitmap(region.Width, region.Height);
            
            using (var graphics = Graphics.FromImage(croppedBitmap))
            {
                graphics.DrawImage(source, new Rectangle(0, 0, region.Width, region.Height), region, GraphicsUnit.Pixel);
            }
            
            return croppedBitmap;
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
    }
}
