﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace CTScanSimulation.Model
{
    public struct Pixel
    {
        private readonly int x;
        private readonly int y;

        public Pixel(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public int X { get { return x; } }
        public int Y { get { return y; } }
    }

    public class CtScan
    {
        private const int padding = 5; // in pixels
        private const int pointSize = 10; // in pixels
        private readonly int centerX;
        private readonly int centerY;
        private readonly float detectorStep;
        private readonly float emitterDetectorSystemStep;
        private readonly int emitterDetectorSystemWidth;
        private readonly int numberOfDetectors;
        private readonly Bitmap originalImage;
        private readonly DirectBitmap directOriginalImage;
        private readonly int radius;
        private readonly long[,] rawData;
        private readonly DirectBitmap sinogram;
        private DirectBitmap recreatedImage;
        private readonly long firstPixelsToSkip;
        private readonly int originalImageWidth;
        private readonly int originalImageHeight;
        private readonly bool filtering;

        public CtScan(Bitmap originalImage, float emitterDetectorSystemStep, int numberOfDetectors, int emitterDetectorSystemWidth, bool filtering)
        {
            ConvertToGreyscale(originalImage);
            directOriginalImage = new DirectBitmap(originalImage);
            this.originalImage = originalImage;
            originalImageWidth = originalImage.Width;
            originalImageHeight = originalImage.Height;
            this.emitterDetectorSystemStep = emitterDetectorSystemStep;
            this.numberOfDetectors = numberOfDetectors;
            this.emitterDetectorSystemWidth = emitterDetectorSystemWidth;
            recreatedImage = new DirectBitmap(originalImage.Width, originalImage.Height);
            detectorStep = (float)emitterDetectorSystemWidth / (numberOfDetectors - 1);
            sinogram = new DirectBitmap(numberOfDetectors, (int)Math.Floor(360 / emitterDetectorSystemStep));
            rawData = new long[originalImage.Width, originalImage.Height];
            this.filtering = filtering;

            centerX = originalImage.Width / 2;
            centerY = originalImage.Height / 2;

            radius = originalImage.Height > originalImage.Width ? originalImage.Width : originalImage.Height;
            radius = radius / 2 - padding;
            firstPixelsToSkip = (long)(2.5 * originalImage.Width / emitterDetectorSystemWidth);
        }

        ~CtScan()
        {
            originalImage.Dispose();
            sinogram.Dispose();
            recreatedImage.Dispose();
            directOriginalImage.Dispose();
        }

        public static long Scale(long originalStart, long originalEnd, // Original range
                                 long newStart, long newEnd,           // Desired range
                                 long value)                           // Value to convert
        {
            double scale = (double)(newEnd - newStart) / (originalEnd - originalStart);
            return (long)(newStart + (value - originalStart) * scale);
        }

        public BitmapImage CreateSinogram(int row)
        {
            CreateSinogramRow(row);
            return sinogram.ToBitmapImage();
        }

        public BitmapImage CreateSinogram()
        {
            int steps = (int)Math.Floor(360 / emitterDetectorSystemStep);
            Parallel.For(0, steps, CreateSinogramRow);
            return sinogram.ToBitmapImage();
        }

        [SuppressMessage("ReSharper", "PossibleLossOfFraction")]
        public BitmapImage DrawCtSystem(int n)
        {
            double angle = n * emitterDetectorSystemStep;
            if (angle > 360)
            {
                throw new ArgumentOutOfRangeException(nameof(n), @"angle>360");
            }
            using (var result = (Bitmap)originalImage.Clone())
            using (Graphics g = Graphics.FromImage(result))
            using (Pen redPen = new Pen(Color.Red, 2))
            using (Pen bluePen = new Pen(Color.CadetBlue, 2))
            {
                double radian = angle * 2 * Math.PI / 360;
                float emitterX = centerX - (float)(Math.Cos(radian) * radius);
                float emitterY = centerY - (float)(Math.Sin(radian) * radius);
                // Draw main frame
                g.DrawEllipse(redPen, centerX - radius, centerY - radius, 2 * radius, 2 * radius);
                // Draw center
                g.FillEllipse(Brushes.Blue, centerX - pointSize / 2, centerY - pointSize / 2, pointSize, pointSize);
                // Draw emitter
                g.FillEllipse(Brushes.Blue, emitterX - pointSize / 2, emitterY - pointSize / 2, pointSize, pointSize);

                for (int i = 0; i < numberOfDetectors; i++)
                {
                    double detectorAngle = angle + (180 - emitterDetectorSystemWidth / 2) + i * detectorStep;
                    double detectorRad = detectorAngle * 2 * Math.PI / 360;
                    float detectorX = centerX - (float)(Math.Cos(detectorRad) * radius);
                    float detectorY = centerY - (float)(Math.Sin(detectorRad) * radius);
                    // Draw detector
                    g.FillEllipse(Brushes.Blue, detectorX - pointSize / 2, detectorY - pointSize / 2, pointSize, pointSize);
                    // Draw ray
                    g.DrawLine(bluePen, emitterX, emitterY, detectorX, detectorY);
                }

                return DirectBitmap.BitmapToBitmapImage(result);
            }
        }

        public BitmapImage RecreateImage()
        {
            Parallel.For(0, sinogram.Height, row =>
            {
                RestoreImageBySinogramRow(row, false);
            });

            CreateBitmapFromRawData();

            return recreatedImage.ToBitmapImage();
        }

        public BitmapImage RecreateImage(int row)
        {
            RestoreImageBySinogramRow(row);
            return recreatedImage.ToBitmapImage();
        }

        private static void ConvertToGreyscale(Bitmap bitmap)
        {
            BitmapData locked = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                                                ImageLockMode.ReadWrite,
                                                PixelFormat.Format32bppArgb);
            unsafe
            {
                byte* pixelPtr = (byte*)(void*)locked.Scan0;

                for (int y = 0; y < bitmap.Height; y++)
                {
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        byte grey = RgbToGreyscale(Color.FromArgb(*pixelPtr, *(pixelPtr + 1), *(pixelPtr + 2)));
                        *pixelPtr++ = grey;
                        *pixelPtr++ = grey;
                        *pixelPtr++ = grey;
                        *pixelPtr++ = 255;
                    }
                }
            }

            bitmap.UnlockBits(locked);
        }

        private static List<Pixel> GetPixelsFromBresenhamLine(int x1, int y1, int x2, int y2)
        {
            // Zmienne pomocnicze
            List<Pixel> pixels = new List<Pixel>();
            int d, dx, dy, ai, bi, xi, yi;
            int x = x1, y = y1;
            // Ustalenie kierunku rysowania
            if (x1 < x2)
            {
                xi = 1;
                dx = x2 - x1;
            }
            else
            {
                xi = -1;
                dx = x1 - x2;
            }
            // Ustalenie kierunku rysowania
            if (y1 < y2)
            {
                yi = 1;
                dy = y2 - y1;
            }
            else
            {
                yi = -1;
                dy = y1 - y2;
            }
            // Pierwszy piksel
            pixels.Add(new Pixel(x, y));
            // Oś wiodąca OX
            if (dx > dy)
            {
                ai = (dy - dx) * 2;
                bi = dy * 2;
                d = bi - dx;
                // Pętla po kolejnych X
                while (x != x2)
                {
                    // Test współczynnika
                    if (d >= 0)
                    {
                        x += xi;
                        y += yi;
                        d += ai;
                    }
                    else
                    {
                        d += bi;
                        x += xi;
                    }
                    pixels.Add(new Pixel(x, y));
                }
            }
            // Oś wiodąca OY
            else
            {
                ai = (dx - dy) * 2;
                bi = dx * 2;
                d = bi - dy;
                //Ppętla po kolejnych Y
                while (y != y2)
                {
                    // Test współczynnika
                    if (d >= 0)
                    {
                        x += xi;
                        y += yi;
                        d += ai;
                    }
                    else
                    {
                        d += bi;
                        y += yi;
                    }
                    pixels.Add(new Pixel(x, y));
                }
            }
            return pixels;
        }

        private static byte RgbToGreyscale(Color c)
        {
            return (byte)((c.R + c.G + c.B) / 3);
        }

        private void CreateBitmapFromRawData()
        {
            // Create new bitmap
            recreatedImage.Dispose();
            recreatedImage = new DirectBitmap(originalImageWidth, originalImageHeight);

            // Find the maximum value in the array
            long maxValue = rawData.Cast<long>().Concat(new long[] { 0 }).Max();

            for (int y = 0; y < originalImageHeight; y++)
            {
                for (int x = 0; x < originalImageWidth; x++)
                {
                    var color = (byte)Scale(0, maxValue, 0, 255, rawData[x, y]);
                    recreatedImage.SetPixel(x, y, Color.FromArgb(color, color, color));
                }
            }
        }

        private void CreateSinogramRow(int row)
        {
            double angle = row * emitterDetectorSystemStep;
            double radian = angle * 2 * Math.PI / 360;
            int emitterX = (int)(centerX - Math.Cos(radian) * radius);
            int emitterY = (int)(centerY - Math.Sin(radian) * radius);

            for (int detectorNo = 0; detectorNo < numberOfDetectors; detectorNo++)
            {
                double detectorAngle = angle + (180 - emitterDetectorSystemWidth / 2) + detectorNo * detectorStep;
                double detectorRad = detectorAngle * 2 * Math.PI / 360;
                int detectorX = (int)(centerX - Math.Cos(detectorRad) * radius);
                int detectorY = (int)(centerY - Math.Sin(detectorRad) * radius);

                List<Pixel> pixelsToSum = GetPixelsFromBresenhamLine(emitterX, emitterY, detectorX, detectorY);
                long sum = pixelsToSum.Aggregate(0, (current, pixel) => current + directOriginalImage.GetPixel(pixel.X, pixel.Y).R);
                // Normalization
                int normalizedSum = (int)(sum / pixelsToSum.Count);
                if (row < sinogram.Height)
                    sinogram.SetPixel(detectorNo, row, Color.FromArgb(normalizedSum, normalizedSum, normalizedSum));
            }
        }

        private void RestoreImageBySinogramRow(int row, bool draw = true)
        {
            double angle = row * emitterDetectorSystemStep;
            double radian = angle * 2 * Math.PI / 360;
            int emitterX = (int)(centerX - Math.Cos(radian) * radius);
            int emitterY = (int)(centerY - Math.Sin(radian) * radius);
            var values = new long[numberOfDetectors];
            for (int i = 0; i < numberOfDetectors; i++)
            {
                values[i] = (long)Math.Pow(sinogram.GetPixel(i, row).R, 1.5);
            }
            if (filtering)
            {
                var kernel = CreateKernel(numberOfDetectors);
                values = ApplyKernel(values, kernel);
            }

            for (int detector = 0; detector < numberOfDetectors; detector++)
            {
                double detectorAngle = angle + (180 - emitterDetectorSystemWidth / 2) + detector * detectorStep;
                double detectorRad = detectorAngle * 2 * Math.PI / 360;
                int detectorX = (int)(centerX - Math.Cos(detectorRad) * radius);
                int detectorY = (int)(centerY - Math.Sin(detectorRad) * radius);

                //Color colorToApply = sinogram.GetPixel(detector, row);
                IEnumerable<Pixel> pixels = GetPixelsFromBresenhamLine(emitterX, emitterY, detectorX, detectorY);

                long skippedPixels = 0;
                // Add colors
                foreach (Pixel pixel in pixels)
                {
                    if (skippedPixels < firstPixelsToSkip)
                    {
                        ++skippedPixels;
                        continue;
                    }
                    Interlocked.Add(ref rawData[pixel.X, pixel.Y], values[detector]);
                }
            }
            if (draw)
            {
                CreateBitmapFromRawData();
            }
        }

        private static long[] ApplyKernel(long[] data, double[] kernel)
        {
            int length = data.Length;
            int middle = kernel.Length / 2;
            var result = new long[length];
            for (int i = 0; i < length; i++)
            {
                int start = middle < i ? 0 : middle - i;
                int end = i + middle < length ? kernel.Length : length - i + 1;
                for (int j = start; j < end; j++)
                {
                    result[i] += (long)(data[i + j - middle] * kernel[j]);
                }
            }
            return result;
        }

        private static double[] CreateKernel(int width)
        {
            if (width % 2 == 0) width++;
            int middle = width / 2;
            var kernel = new double[width];
            for (int i = 0; i < width; i++)
            {
                int distanceFromMiddle = Math.Abs(i - middle);
                if (distanceFromMiddle == 0) kernel[i] = 1;
                else if (distanceFromMiddle % 2 == 0) kernel[i] = 0;
                else kernel[i] = -4 / (Math.PI * Math.PI * distanceFromMiddle * distanceFromMiddle);
            }
            return kernel;
        }

        public double CalculateMeanSquaredError()
        {
            double sum = 0;
            int numberOfPixels = originalImageHeight * originalImageWidth;
            for (int y = 0; y < originalImageHeight; y++)
            {
                for (int x = 0; x < originalImageWidth; x++)
                {
                    var orginalColor = originalImage.GetPixel(x, y);
                    var recreatedColor = recreatedImage.GetPixel(x, y);
                    sum += (orginalColor.R - recreatedColor.R) * (orginalColor.R - recreatedColor.R);
                }
            }
            return sum / numberOfPixels;
        }
    }
}