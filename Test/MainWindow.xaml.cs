using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Test
{
    public partial class MainWindow : Window
    {
        private double zoomScale = 1.0;
        private PreviewWindow? previewWindow;
        private int[] mouseXY = new int[2];
        private BitmapSource? templateSource;
        [DllImport("NativeImageProcessing.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern long CalculateSSD_SSE2CPP(byte[] originalGray, byte[] templateGray, int originalStartIndex, int templateStartIndex, int length);
        [DllImport("NativeImageProcessing.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern long CalculateTemplateSSD_SSE2CPP(byte[] originalGray, byte[] templateGray, int originalWidth, int templateWidth, int templateHeight, int x, int y, long bestscore);
        [DllImport("NativeImageProcessing.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern long FindBestMatch_SSE2CPP(byte[] originalGray, byte[] templateGray, int originalWidth, int originalHeight, int templateWidth, int templateHeight, out int bestX, out int bestY);
        public MainWindow()
        {
            InitializeComponent();
        }
        private void OpenButton_Click(object sender, RoutedEventArgs e)
        {

            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp";
            if (dialog.ShowDialog() == true)
            {
                if (previewWindow != null && previewWindow.IsVisible)
                {
                    previewWindow.Close();
                    previewWindow = null;
                }
                BitmapImage bitmap = new BitmapImage(new Uri(dialog.FileName));
                OriginalImage.Source = bitmap;
                ResultImage.Source = bitmap;
                zoomScale = 1.0;
                ResultImage.LayoutTransform = new ScaleTransform(zoomScale, zoomScale);
            }
        }
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            BitmapSource? save = ResultImage.Source as BitmapSource;
            if (save == null)
            {
                MessageBox.Show("저장할 이미지가 없습니다.");
                return;
            }
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.DefaultExt = ".png";
            if (dialog.ShowDialog() == true)
            {
                using (FileStream stream = new FileStream(dialog.FileName, FileMode.Create))
                {
                    PngBitmapEncoder encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(save));
                    encoder.Save(stream);
                }
            }
        }

        private void zoomInButton_Click(object sender, RoutedEventArgs e)
        {
            zoomScale += 0.1;
            ResultImageLayer.LayoutTransform = new ScaleTransform(zoomScale, zoomScale);
        }
        private void zoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            if (zoomScale > 0.2)
            {
                zoomScale -= 0.1;
                ResultImageLayer.LayoutTransform = new ScaleTransform(zoomScale, zoomScale);
            }
            else
            {
                MessageBox.Show("더 이상 축소할 수 없습니다.");
            }
        }
        private void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (ResultImage.Source == null)
            {
                MessageBox.Show("미리보기할 이미지가 없습니다.");
                return;
            }
            if (previewWindow == null || !previewWindow.IsVisible)
            {
                previewWindow = new PreviewWindow();
                previewWindow.SetPreviewImage(ResultImage.Source);
                previewWindow.Show();
            }
            else
            {
                previewWindow.Activate();
            }
        }

        private void ResultImage_MouseMove(object sender, MouseEventArgs e)
        {
            Point position = e.GetPosition(ResultImage);
            mouseXY[0] = (int)position.X;
            mouseXY[1] = (int)position.Y;

            if (previewWindow != null && previewWindow.IsVisible)
            {
                previewWindow.Title = $"X:{mouseXY[0]},Y:{mouseXY[1]}";
                BitmapSource? source = ResultImage.Source as BitmapSource;
                if (source == null)
                {
                    return;
                }
                int cropSize = 100;
                int half = cropSize / 2;

                int x = mouseXY[0] - half;
                int y = mouseXY[1] - half;

                if (x < 0)
                {
                    x = 0;
                }
                if (y < 0)
                {
                    y = 0;
                }


                if (x + cropSize > source.PixelWidth)
                {
                    x = source.PixelWidth - cropSize;
                }


                if (y + cropSize > source.PixelHeight)
                {
                    y = source.PixelHeight - cropSize;
                }

                Int32Rect rect = new Int32Rect(x, y, cropSize, cropSize);
                CroppedBitmap cropped = new CroppedBitmap(source, rect);

                previewWindow.SetPreviewImage(cropped);
            }
        }
        private void ResultImage_MouseLeave(object sender, MouseEventArgs e)
        {
            if (previewWindow != null && previewWindow.IsVisible)
            {
                if (ResultImage.Source != null)
                {
                    previewWindow.SetPreviewImage(ResultImage.Source);
                    previewWindow.Title = "PreviewWindow";
                }
            }
        }
        private void BinarizationButton_Click(object sender, RoutedEventArgs e)
        {
            BitmapSource? source = ResultImage.Source as BitmapSource;
            if (source == null)
            {
                MessageBox.Show("이진화할 이미지가 없습니다.");
                return;
            }
            FormatConvertedBitmap converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = converted.PixelWidth;
            int height = converted.PixelHeight;
            int bytesPerPixel = 4;
            int stride = width * bytesPerPixel;
            byte[] pixels = new byte[height * stride];

            converted.CopyPixels(pixels, stride, 0);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * stride + x * 4;
                    byte B = pixels[index];
                    byte G = pixels[index + 1];
                    byte R = pixels[index + 2];
                    int gray = (B + G + R) / 3;
                    byte value = (byte)(gray >= 128 ? 255 : 0);
                    pixels[index] = value;
                    pixels[index + 1] = value;
                    pixels[index + 2] = value;
                }
            }
            WriteableBitmap binaryImage = new WriteableBitmap(width, height, converted.DpiX, converted.DpiY, PixelFormats.Bgra32, null);
            Int32Rect rect = new Int32Rect(0, 0, width, height);
            binaryImage.WritePixels(rect, pixels, stride, 0);
            ResultImage.Source = binaryImage;

        }
        private void SmoothingButton_Click(object sender, RoutedEventArgs e)
        {
            BitmapSource? source = ResultImage.Source as BitmapSource;
            if (source == null)
            {
                MessageBox.Show("평활화할 이미지가 없습니다.");
                return;
            }
            FormatConvertedBitmap converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = converted.PixelWidth;
            int height = converted.PixelHeight;
            int bytesPerPixel = 4;
            int stride = width * bytesPerPixel;
            byte[] pixels = new byte[height * stride];
            converted.CopyPixels(pixels, stride, 0);
            byte[] smoothedPixels = new byte[height * stride];
            Array.Copy(pixels, smoothedPixels, pixels.Length);
            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    int index = y * stride + x * 4;
                    int sumB = 0, sumG = 0, sumR = 0;
                    for (int ky = -1; ky <= 1; ky++)
                    {
                        for (int kx = -1; kx <= 1; kx++)
                        {
                            int neighborIndex = (y + ky) * stride + (x + kx) * 4;
                            sumB += pixels[neighborIndex];
                            sumG += pixels[neighborIndex + 1];
                            sumR += pixels[neighborIndex + 2];
                        }
                    }
                    smoothedPixels[index] = (byte)(sumB / 9);
                    smoothedPixels[index + 1] = (byte)(sumG / 9);
                    smoothedPixels[index + 2] = (byte)(sumR / 9);
                }
            }
            WriteableBitmap smoothedImage = new WriteableBitmap(width, height, converted.DpiX, converted.DpiY, PixelFormats.Bgra32, null);
            Int32Rect rect = new Int32Rect(0, 0, width, height);
            smoothedImage.WritePixels(rect, smoothedPixels, stride, 0);
            ResultImage.Source = smoothedImage;
        }

        private double[,] CreateGaussianKernel(int size, double sigma)
        {
            double[,] kernel = new double[size, size];
            int radius = size / 2;
            double sum = 0;

            for(int y = -radius; y <= radius; y++)
            {
                for(int x = -radius; x <= radius; x++)
                {
                    double value = Math.Exp(-(x * x + y * y) / (2 * sigma * sigma)) / (2 * Math.PI * sigma * sigma);
                    kernel[y + radius, x + radius] = value;
                    sum += value;
                }
            }
            for(int y = 0; y < size; y++)
            {
                for(int x = 0; x < size; x++)
                {
                    kernel[y, x] /= sum;
                }
            }
            return kernel;
        }

        private void GaussianButton_Click(object sender, EventArgs e)
        {
            if (ResultImage.Source is not BitmapSource source)
            {
                MessageBox.Show("가우시안화할 이미지가 없습니다.");
                return;
            }
            FormatConvertedBitmap gaussian = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = gaussian.PixelWidth;
            int height = gaussian.PixelHeight;

            const int bytesPerPixel = 4;
            int stride = width * bytesPerPixel;

            byte[] pixels = new byte[height * stride];
            gaussian.CopyPixels(pixels, stride, 0);

            byte[] gaussianPixels = (byte[])pixels.Clone();

            double[,] kernel = CreateGaussianKernel(5, 1.0);
            int radius = kernel.GetLength(0) / 2;

            for (int y = radius; y < height - radius; y++)
            {
                for (int x = radius; x < width - radius; x++)
                {
                    double sumB = 0; double sumG = 0; double sumR = 0;

                    for (int ky = -radius; ky <= radius; ky++)
                    {
                        for (int kx = -radius; kx <= radius; kx++)
                        {
                            double weight =
                                kernel[ky + radius, kx + radius];

                            int neighborIndex =
                                (y + ky) * stride
                                + (x + kx) * bytesPerPixel;

                            sumB += pixels[neighborIndex] * weight;
                            sumG += pixels[neighborIndex + 1] * weight;
                            sumR += pixels[neighborIndex + 2] * weight;
                        }
                    }
                    int index = y * stride + x * bytesPerPixel;
                    gaussianPixels[index] = ToByte(sumB);
                    gaussianPixels[index + 1] = ToByte(sumG);
                    gaussianPixels[index + 2] = ToByte(sumR);
                }
            }
            WriteableBitmap gaussianImage = new WriteableBitmap(width, height, gaussian.DpiX, gaussian.DpiY, PixelFormats.Bgra32, null);

            gaussianImage.WritePixels( new Int32Rect(0, 0, width, height), gaussianPixels, stride, 0);

            ResultImage.Source = gaussianImage;
        }

        private byte ToByte(double value)
        {
            value = Math.Round(value);
            value = Math.Max(0, Math.Min(255, value));

            return (byte)value;
        }

        private void LaplaceButton_Click(object sender, EventArgs e)
        {
            BitmapSource? source = ResultImage.Source as BitmapSource;
            if (source == null)
            {
                MessageBox.Show("라플라스화할 이미지가 없습니다.");
                return;
            }
            FormatConvertedBitmap laplace = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = laplace.PixelWidth;
            int height = laplace.PixelHeight;
            int bytesPerPixel = 4;
            int stride = width * bytesPerPixel;
            byte[] pixels = new byte[height * stride];
            laplace.CopyPixels(pixels, stride, 0);
            byte[] laplacePixels = new byte[height * stride];
            Array.Copy(pixels, laplacePixels, pixels.Length);
            int[,] kernel =
            {
                { 0, -1, 0 },
                { -1, 4, -1 },
                { 0, -1, 0 }
            };
            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    int index = y * stride + x * 4;
                    int sumB = 0, sumG = 0, sumR = 0;
                    for (int ky = -1; ky <= 1; ky++)
                    {
                        for (int kx = -1; kx <= 1; kx++)
                        {
                            int weight = kernel[ky + 1, kx + 1];
                            int neighborIndex = (y + ky) * stride + (x + kx) * 4;
                            sumB += pixels[neighborIndex] * weight;
                            sumG += pixels[neighborIndex + 1] * weight;
                            sumR += pixels[neighborIndex + 2] * weight;
                        }
                    }
                    sumB = Math.Clamp(sumB, 0, 255);
                    sumG = Math.Clamp(sumG, 0, 255);
                    sumR = Math.Clamp(sumR, 0, 255);
                    laplacePixels[index] = (byte)(sumB);
                    laplacePixels[index + 1] = (byte)(sumG);
                    laplacePixels[index + 2] = (byte)(sumR);
                }
            }
            WriteableBitmap laplaceImage = new WriteableBitmap(width, height, laplace.DpiX, laplace.DpiY, PixelFormats.Bgra32, null);
            Int32Rect rect = new Int32Rect(0, 0, width, height);
            laplaceImage.WritePixels(rect, laplacePixels, stride, 0);
            ResultImage.Source = laplaceImage;
        }

        private void SobelButton_Click(object sender, EventArgs e)
        {
            BitmapSource? source = ResultImage.Source as BitmapSource;
            if (source == null)
            {
                MessageBox.Show("소벨화할 이미지가 없습니다.");
                return;
            }
            FormatConvertedBitmap sobel = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = sobel.PixelWidth;
            int height = sobel.PixelHeight;
            int bytesPerPixel = 4;
            int stride = width * bytesPerPixel;
            byte[] pixels = new byte[height * stride];
            sobel.CopyPixels(pixels, stride, 0);
            byte[] sobelPixels = new byte[height * stride];
            Array.Copy(pixels, sobelPixels, pixels.Length);
            int[,] kernel_x =
            {
                { -1, 0, 1 },
                { -2, 0, 2 },
                { -1, 0, 1 }
            };
            int[,] kernel_y =
            {
                { -1, -2, -1 },
                { 0, 0, 0 },
                { 1, 2, 1 }
            };
            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    int index = y * stride + x * 4;
                    int sumB_x = 0, sumG_x = 0, sumR_x = 0;
                    int sumB_y = 0, sumG_y = 0, sumR_y = 0;
                    for (int ky = -1; ky <= 1; ky++)
                    {
                        for (int kx = -1; kx <= 1; kx++)
                        {
                            int weight_x = kernel_x[ky + 1, kx + 1];
                            int weight_y = kernel_y[ky + 1, kx + 1];
                            int neighborIndex = (y + ky) * stride + (x + kx) * 4;
                            sumB_x += pixels[neighborIndex] * weight_x;
                            sumG_x += pixels[neighborIndex + 1] * weight_x;
                            sumR_x += pixels[neighborIndex + 2] * weight_x;
                            sumB_y += pixels[neighborIndex] * weight_y;
                            sumG_y += pixels[neighborIndex + 1] * weight_y;
                            sumR_y += pixels[neighborIndex + 2] * weight_y;
                        }
                    }
                    int magnitudeB = (int)Math.Sqrt(sumB_x * sumB_x + sumB_y * sumB_y);
                    int magnitudeG = (int)Math.Sqrt(sumG_x * sumG_x + sumG_y * sumG_y);
                    int magnitudeR = (int)Math.Sqrt(sumR_x * sumR_x + sumR_y * sumR_y);
                    sobelPixels[index] = (byte)Math.Clamp(magnitudeB, 0, 255);
                    sobelPixels[index + 1] = (byte)Math.Clamp(magnitudeG, 0, 255);
                    sobelPixels[index + 2] = (byte)Math.Clamp(magnitudeR, 0, 255);
                }
            }
            WriteableBitmap sobleimage = new WriteableBitmap(width, height, sobel.DpiX, sobel.DpiY, PixelFormats.Bgra32, null);
            Int32Rect rect = new Int32Rect(0, 0, width, height);
            sobleimage.WritePixels(rect, sobelPixels, stride, 0);
            ResultImage.Source = sobleimage;
        }
        private void FFT1D(Complex[] buffer)
        {
            int n = buffer.Length;
            if ((n & (n - 1)) != 0)
            {
                throw new ArgumentException("FFT 길이는 2의 거듭제곱이어야 합니다.");
            }
            int j = 0;
            for (int i = 1; i < n; i++)
            {
                int bit = n >> 1;
                while ((j & bit) != 0)
                {
                    j ^= bit;
                    bit >>= 1;
                }
                j ^= bit;
                if (i < j)
                {
                    Complex temp = buffer[i];
                    buffer[i] = buffer[j];
                    buffer[j] = temp;
                }
            }
            for (int len = 2; len <= n; len *= 2)
            {
                double angle = -2 * Math.PI / len;
                Complex wlen = new Complex(Math.Cos(angle), Math.Sin(angle));

                for (int i = 0; i < n; i += len)
                {
                    Complex w = Complex.One;
                    for (int j1 = 0; j1 < len / 2; j1++)
                    {
                        Complex u = buffer[i + j1];
                        Complex v = buffer[i + j1 + len / 2] * w;
                        buffer[i + j1] = u + v;
                        buffer[i + j1 + len / 2] = u - v;
                        w *= wlen;
                    }
                }
            }
        }
        private int GetNextPowerOfTwo(int n)
        {
            int nextPowerOfTwo = 1;
            while (nextPowerOfTwo < n)
            {
                nextPowerOfTwo *= 2;
            }
            return nextPowerOfTwo;
        }
        private void FFTButton_Click(object sender, EventArgs e)
        {
            BitmapSource? source = ResultImage.Source as BitmapSource;
            if (source == null)
            {
                MessageBox.Show("FFT할 이미지가 없습니다.");
                return;
            }
            FormatConvertedBitmap converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = converted.PixelWidth;
            int height = converted.PixelHeight;
            int fftwidth = GetNextPowerOfTwo(width);
            int fftheight = GetNextPowerOfTwo(height);
            int bytesPerPixel = 4;
            int stride = width * bytesPerPixel;
            int fftstride = fftwidth * 4;
            byte[] pixels = new byte[height * stride];
            byte[] fftPixels = new byte[fftheight * fftstride];
            double[,] magnitude = new double[fftheight, fftwidth];
            double max = 0;
            Complex[,] fftData = new Complex[fftheight, fftwidth];

            converted.CopyPixels(pixels, stride, 0);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {

                    int index = y * stride + x * 4;
                    byte B = pixels[index];
                    byte G = pixels[index + 1];
                    byte R = pixels[index + 2];
                    int gray = (B + G + R) / 3;
                    pixels[index] = (byte)gray;
                    pixels[index + 1] = (byte)gray;
                    pixels[index + 2] = (byte)gray;
                    fftData[y, x] = new Complex(gray, 0);
                }
            }
            for (int y = 0; y < fftheight; y++)
            {
                Complex[] row = new Complex[fftwidth];

                for (int x = 0; x < fftwidth; x++)
                {
                    row[x] = fftData[y, x];
                }
                FFT1D(row);
                for (int x = 0; x < fftwidth; x++)
                {
                    fftData[y, x] = row[x];
                }
            }

            for (int x = 0; x < fftwidth; x++)
            {
                Complex[] column = new Complex[fftheight];
                for (int y = 0; y < fftheight; y++)
                {
                    column[y] = fftData[y, x];
                }
                FFT1D(column);
                for (int y = 0; y < fftheight; y++)
                {
                    fftData[y, x] = column[y];
                }
            }
            for (int y = 0; y < fftheight; y++)
            {
                for (int x = 0; x < fftwidth; x++)
                {
                    double value = Math.Log(1 + fftData[y, x].Magnitude);
                    magnitude[y, x] = value;
                    if (magnitude[y, x] > max)
                    {
                        max = value;
                    }
                }
            }
            for (int y = 0; y < fftheight; y++)
            {
                for (int x = 0; x < fftwidth; x++)
                {
                    int index = y * fftstride + x * 4;
                    int shiftedX = (x + fftwidth / 2) % fftwidth;
                    int shiftedY = (y + fftheight / 2) % fftheight;
                    byte gray = (byte)(magnitude[shiftedY, shiftedX] / max * 255);
                    fftPixels[index] = gray;
                    fftPixels[index + 1] = gray;
                    fftPixels[index + 2] = gray;
                    fftPixels[index + 3] = 255;
                }
            }
            WriteableBitmap fftimage = new WriteableBitmap(fftwidth, fftheight, converted.DpiX, converted.DpiY, PixelFormats.Bgra32, null);
            Int32Rect rect = new Int32Rect(0, 0, fftwidth, fftheight);
            fftimage.WritePixels(rect, fftPixels, fftstride, 0);
            ResultImage.Source = fftimage;
        }

        private void TemplateButton_Click(object sender, EventArgs e)
        {
            ResultOverlay.Children.Clear();
            BitmapSource? source = OriginalImage.Source as BitmapSource;
            if (source == null)
            {
                MessageBox.Show("원본이미지를 먼저 여십시오.");
                return;
            }
            OpenFileDialog dialog = new OpenFileDialog();

            dialog.Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp";
            if (dialog.ShowDialog() == true)
            {
                BitmapImage bitmap = new BitmapImage(new Uri(dialog.FileName));
                templateSource = bitmap;
                TemplatePreviewImage.Source = templateSource;
            }
        }
        private void TemplateMatchingButton_Click(object sender, EventArgs e)
        {

            if (templateSource == null)
            {
                MessageBox.Show("템플릿 이미지를 먼저 선택하십시오.");
                return;
            }
            BitmapSource? source = OriginalImage.Source as BitmapSource;
            if (source == null)
            {
                MessageBox.Show("원본이미지를 먼저 여십시오.");
                return;
            }
            FormatConvertedBitmap orignalconverted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            FormatConvertedBitmap templateconverted = new FormatConvertedBitmap(templateSource, PixelFormats.Bgra32, null, 0);
            int originalWidth = orignalconverted.PixelWidth;
            int originalHeight = orignalconverted.PixelHeight;
            int templateWidth = templateconverted.PixelWidth;
            int templateHeight = templateconverted.PixelHeight;
            int bytesPerPixel = 4;
            int originalStride = originalWidth * bytesPerPixel;
            int templateStride = templateWidth * bytesPerPixel;
            byte[] originalpixels = new byte[originalHeight * originalStride];
            byte[] templatepixels = new byte[templateHeight * templateStride];
            byte[] resultpiexls = new byte[originalHeight * originalStride];
            orignalconverted.CopyPixels(originalpixels, originalStride, 0);
            templateconverted.CopyPixels(templatepixels, templateStride, 0);
            Array.Copy(originalpixels, resultpiexls, originalpixels.Length);
            if (templateWidth > originalWidth || templateHeight > originalHeight)
            {
                MessageBox.Show("템플릿 이미지가 원본 이미지보다 큽니다.");
                return;
            }
            for (int y = 0; y < originalHeight; y++)
            {
                for (int x = 0; x < originalWidth; x++)
                {
                    int index = y * originalStride + x * 4;
                    byte B = originalpixels[index];
                    byte G = originalpixels[index + 1];
                    byte R = originalpixels[index + 2];
                    int gray = (B + G + R) / 3;
                    originalpixels[index] = (byte)gray;
                    originalpixels[index + 1] = (byte)gray;
                    originalpixels[index + 2] = (byte)gray;
                }
            }
            for (int y = 0; y < templateHeight; y++)
            {
                for (int x = 0; x < templateWidth; x++)
                {
                    int index = y * templateStride + x * 4;
                    byte B = templatepixels[index];
                    byte G = templatepixels[index + 1];
                    byte R = templatepixels[index + 2];
                    int gray = (B + G + R) / 3;
                    templatepixels[index] = (byte)gray;
                    templatepixels[index + 1] = (byte)gray;
                    templatepixels[index + 2] = (byte)gray;
                }
            }
            long bestscore = long.MaxValue;
            int bestX = 0;
            int bestY = 0;
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            for (int y = 0; y <= originalHeight - templateHeight; y++)
            {
                for (int x = 0; x <= originalWidth - templateWidth; x++)
                {
                    long score = 0;
                    bool stop = false;
                    for (int ty = 0; ty < templateHeight; ty++)
                    {

                        for (int tx = 0; tx < templateWidth; tx++)
                        {

                            int originalIndex = (y + ty) * originalStride + (x + tx) * 4;
                            int templateIndex = ty * templateStride + tx * 4;
                            int diffB = originalpixels[originalIndex] - templatepixels[templateIndex];
                            score += diffB * diffB;
                            if (score >= bestscore)
                            {
                                stop = true;
                                break;
                            }
                        }
                            if (stop)
                            {
                                break;
                            }
                    }
                        if (score < bestscore)
                        {
                            bestscore = score;
                            bestX = x;
                            bestY = y;
                        }
                }
            }
            
            stopwatch.Stop();
            for (int x = bestX; x < bestX + templateWidth; x++)
            {
                int topIndex = bestY * originalStride + x * 4;
                int bottomIndex = (bestY + templateHeight - 1) * originalStride + x * 4;
                resultpiexls[topIndex] = 0;
                resultpiexls[topIndex + 1] = 0;
                resultpiexls[topIndex + 2] = 255;
                resultpiexls[topIndex + 3] = 255;
                resultpiexls[bottomIndex] = 0;
                resultpiexls[bottomIndex + 1] = 0;
                resultpiexls[bottomIndex + 2] = 255;
                resultpiexls[bottomIndex + 3] = 255;
            }
            for (int y = bestY; y < bestY + templateHeight; y++)
            {
                int leftIndex = y * originalStride + bestX * 4;
                int rightIndex = y * originalStride + (bestX + templateWidth - 1) * 4;
                resultpiexls[leftIndex] = 0;
                resultpiexls[leftIndex + 1] = 0;
                resultpiexls[leftIndex + 2] = 255;
                resultpiexls[leftIndex + 3] = 255;
                resultpiexls[rightIndex] = 0;
                resultpiexls[rightIndex + 1] = 0;
                resultpiexls[rightIndex + 2] = 255;
                resultpiexls[rightIndex + 3] = 255;
            }
            WriteableBitmap resultImage = new WriteableBitmap(originalWidth, originalHeight, orignalconverted.DpiX, orignalconverted.DpiY, PixelFormats.Bgra32, null);
            Int32Rect rect = new Int32Rect(0, 0, originalWidth, originalHeight);
            resultImage.WritePixels(rect, resultpiexls, originalStride, 0);

            ResultImage.Source = resultImage;
            ResultScrollViewer.ScrollToHorizontalOffset(bestX * zoomScale);
            ResultScrollViewer.ScrollToVerticalOffset(bestY * zoomScale);
            MessageBox.Show($"템플릿 매칭 시간: {stopwatch.ElapsedMilliseconds} ms");
        }
        private long CalcurateSSD_SIMD(byte[] originalGray, byte[] templateGray, int originalStartIndex, int templateStartIndex, int length)
        {
            long score = 0;
            int i = 0;
            int vectorSize = Vector<int>.Count;
            int[] originalTemp = new int[vectorSize];
            int[] templateTemp = new int[vectorSize];

            for (; i <= length - vectorSize; i += vectorSize)
            {
                for (int j = 0; j < vectorSize; j++)
                {
                    originalTemp[j] = originalGray[originalStartIndex + i + j];
                    templateTemp[j] = templateGray[templateStartIndex + i + j];

                }

                Vector<int> originalVector = new Vector<int>(originalTemp);
                Vector<int> templateVector = new Vector<int>(templateTemp);

                Vector<int> diffVector = originalVector - templateVector;
                Vector<int> squaredDiffVector = diffVector * diffVector;
                for (int j = 0; j < vectorSize; j++)
                {
                    score += squaredDiffVector[j];
                }
            }
            for (; i < length; i++)
            {
                int diff = originalGray[originalStartIndex + i] - templateGray[templateStartIndex + i];
                score += diff * diff;
            }
            return score;
        }
        private unsafe void SSETestButton_Click(object sender, EventArgs e)
        {
            byte[] originalGray = { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150, 160 };
            byte[] templateGray = { 12, 18, 33, 35, 49, 62, 72, 78, 91, 99, 105, 125, 128, 141, 149, 170 };
            //int originalStartIndex = 0;
            //int templateStartIndex = 0;
            int length = originalGray.Length;
            int testscore = 0;
            for (int i = 0; i < length; i++)
            {
                int Result = originalGray[i] - templateGray[i];
                testscore += Result * Result;
            }

            //CalcurateSSD_SSE2(originalGray, templateGray, originalStartIndex, templateStartIndex, length);  
            fixed (byte* pOriginal = originalGray)
            fixed (byte* pTemplate = templateGray)
            {
                Vector128<byte> originalvector = Sse2.LoadVector128(pOriginal);
                Vector128<byte> templatevector = Sse2.LoadVector128(pTemplate);

                Vector128<byte> zero = Vector128<byte>.Zero;

                Vector128<ushort> originalLow = Sse2.UnpackLow(originalvector, zero).AsUInt16();
                Vector128<ushort> originalHigh = Sse2.UnpackHigh(originalvector, zero).AsUInt16();

                Vector128<ushort> templateLow = Sse2.UnpackLow(templatevector, zero).AsUInt16();
                Vector128<ushort> templateHigh = Sse2.UnpackHigh(templatevector, zero).AsUInt16();



                ushort originalFirstLow = originalLow.GetElement(0);
                ushort originalFirstHigh = originalHigh.GetElement(0);
                ushort templateFirstLow = templateLow.GetElement(0);
                ushort templateFirstHigh = templateHigh.GetElement(0);

                Vector128<short> diffLow = Sse2.Subtract(originalLow.AsInt16(), templateLow.AsInt16());
                Vector128<short> diffHigh = Sse2.Subtract(originalHigh.AsInt16(), templateHigh.AsInt16());

                short diffLow0 = diffLow.GetElement(0);
                short diffHigh0 = diffHigh.GetElement(0);

                Vector128<ushort> squareLow = Sse2.MultiplyLow(diffLow, diffLow).AsUInt16();
                Vector128<ushort> squareHigh = Sse2.MultiplyLow(diffHigh, diffHigh).AsUInt16();

                ushort squareLow0 = squareLow.GetElement(0);
                ushort squareHigh0 = squareHigh.GetElement(0);
                long sseScore = 0;
                for (int k = 0; k < 8; k++)
                {
                    sseScore += squareLow.GetElement(k);
                    sseScore += squareHigh.GetElement(k);
                }
                MessageBox.Show($"Normal : {testscore}, SSE : {sseScore}");

            }
        }

        private unsafe long CalcurateSSD_SSE2(byte[] originalGray, byte[] templateGray, int originalStartIndex, int templateStartIndex, int length)
        {
            long score = 0;
            int i = 0;
            fixed (byte* pOriginal = originalGray)
            fixed (byte* pTemplate = templateGray)
            {
                Vector128<byte> zero = Vector128<byte>.Zero;
                for (; i <= length - 16; i += 16)
                {
                    Vector128<byte> originalVector = Sse2.LoadVector128(pOriginal + originalStartIndex + i);
                    Vector128<byte> templateVector = Sse2.LoadVector128(pTemplate + templateStartIndex + i);
                    Vector128<ushort> originalLow = Sse2.UnpackLow(originalVector, zero).AsUInt16();
                    Vector128<ushort> originalHigh = Sse2.UnpackHigh(originalVector, zero).AsUInt16();
                    Vector128<ushort> templateLow = Sse2.UnpackLow(templateVector, zero).AsUInt16();
                    Vector128<ushort> templateHigh = Sse2.UnpackHigh(templateVector, zero).AsUInt16();
                    Vector128<short> diffLow = Sse2.Subtract(originalLow.AsInt16(), templateLow.AsInt16());
                    Vector128<short> diffHigh = Sse2.Subtract(originalHigh.AsInt16(), templateHigh.AsInt16());
                    Vector128<ushort> squareLow = Sse2.MultiplyLow(diffLow, diffLow).AsUInt16();
                    Vector128<ushort> squareHigh = Sse2.MultiplyLow(diffHigh, diffHigh).AsUInt16();
                    for (int j = 0; j < 8; j++)
                    {
                        score += squareLow.GetElement(j);
                        score += squareHigh.GetElement(j);
                    }
                }

            }
            for (; i < length; i++)
            {
                int diff = originalGray[originalStartIndex + i] - templateGray[templateStartIndex + i];
                score += diff * diff;
            }
            return score;
        }


        private void SSETemplateMatchingButton_Click(object sender, EventArgs e) //c# 함수(SSE)만 사용하는 템플릿 매칭
        {
            ResultOverlay.Children.Clear();
            if (templateSource == null)
            {
                MessageBox.Show("템플릿 이미지를 먼저 선택하십시오.");
                return;
            }
            BitmapSource? source = OriginalImage.Source as BitmapSource;
            if (source == null)
            {
                MessageBox.Show("원본이미지를 먼저 여십시오.");
                return;
            }
            BitmapSource? template = templateSource;

            double scale = 0.7;
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            BitmapSource smallOriginal = MakeSmallBitmap(source, scale);
            BitmapSource smallTemplate = MakeSmallBitmap(template, scale);

            byte[] smallOriginalGrey = MarkGrayPixels(smallOriginal, out int smallOriginalWidth, out int smallOriginalHeight);
            byte[] smallTemplateGrey = MarkGrayPixels(smallTemplate, out int smallTemplateWidth, out int smallTemplateHeight);

            FindBestMatch(smallOriginalGrey, smallOriginalWidth, smallOriginalHeight, smallTemplateGrey, smallTemplateWidth, smallTemplateHeight, out int smallbestX, out int smallbestY);

            int roughX = (int)(smallbestX / scale);
            int roughY = (int)(smallbestY / scale);

            int searchRange = 300;
            int cropX = roughX - searchRange;
            int cropY = roughY - searchRange;
            if (cropX < 0)
            {
                cropX = 0;
            }
            if (cropY < 0)
            {
                cropY = 0;
            }
            int cropWidth = template.PixelWidth + searchRange * 2;
            int cropHeight = template.PixelHeight + searchRange * 2;
            if (cropX + cropWidth > source.PixelWidth)
            {
                cropWidth = source.PixelWidth - cropX;
            }
            if (cropY + cropHeight > source.PixelHeight)
            {
                cropHeight = source.PixelHeight - cropY;
            }
            Int32Rect cropRect = new Int32Rect(cropX, cropY, cropWidth, cropHeight);
            CroppedBitmap croppedSource = new CroppedBitmap(source, cropRect);

            byte[] cropGrey = MarkGrayPixels(croppedSource, out int cropWidth2, out int cropHeight2);
            byte[] templateGrey = MarkGrayPixels(template, out int templateWidth, out int templateHeight);
            CsharpFindBestMatch(cropGrey, cropWidth2, cropHeight2, templateGrey, templateWidth, templateHeight, out int localbestX, out int localbestY);
            int finalX = cropX + localbestX;
            int finalY = cropY + localbestY;
            ResultImage.Source = source;
            DrawMatchRectangle(finalX, finalY, template.PixelWidth, template.PixelHeight);
            stopwatch.Stop();
            ResultScrollViewer.ScrollToHorizontalOffset(finalX * zoomScale);
            ResultScrollViewer.ScrollToVerticalOffset(finalY * zoomScale);
            MessageBox.Show($"템플릿 매칭 시간: {stopwatch.ElapsedMilliseconds} ms\nrough: {roughX}, {roughY}\nfinal: {finalX}, {finalY}");
        }

        private unsafe long CalcurateTemplateSSD_SSE2(byte[] originalGray, byte[] templateGray, int originalWidth, int templateWidth, int templateHeight, int x, int y, long bestscore)
        {
            long score = 0;

            for (int ty = 0; ty < templateHeight; ty++)
            {
                int originalStartIndex = (y + ty) * originalWidth + x;
                int templateStartIndex = ty * templateWidth;

                score += CalcurateSSD_SSE2(originalGray, templateGray, originalStartIndex, templateStartIndex, templateWidth);

                if (score >= bestscore)
                {
                    break;
                }
            }
            return score;
        }
        private long CalcurateTemplateSSD_SSE2_CPP(byte[] originalGray, byte[] templateGray, int originalWidth, int templateWidth, int templateHeight, int x, int y, long bestscore)
        {
            long score = 0;
            for (int ty = 0; ty < templateHeight; ty++)
            {
                int originalStartIndex = (y + ty) * originalWidth + x;
                int templateStartIndex = ty * templateWidth;

                score += CalculateSSD_SSE2CPP(originalGray, templateGray, originalStartIndex, templateStartIndex, templateWidth);

                if (score >= bestscore)
                {
                    break;
                }
            }

            return score;
        }
        private void RealSSETemplateMatchingButton_Click(object sender, EventArgs e)
        {
            ResultOverlay.Children.Clear();
            if (templateSource == null)
            {
                MessageBox.Show("템플릿 이미지를 먼저 선택하십시오.");
                return;
            }
            BitmapSource? source = OriginalImage.Source as BitmapSource;
            if (source == null)
            {
                MessageBox.Show("원본이미지를 먼저 여십시오.");
                return;
            }
            BitmapSource? template = templateSource;

            double scale = 0.7;
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            BitmapSource smallOriginal = MakeSmallBitmap(source, scale);
            BitmapSource smallTemplate = MakeSmallBitmap(template, scale);

            byte[] smallOriginalGrey = MarkGrayPixels(smallOriginal, out int smallOriginalWidth, out int smallOriginalHeight);
            byte[] smallTemplateGrey = MarkGrayPixels(smallTemplate, out int smallTemplateWidth, out int smallTemplateHeight);

            FindBestMatch(smallOriginalGrey, smallOriginalWidth, smallOriginalHeight, smallTemplateGrey, smallTemplateWidth, smallTemplateHeight, out int smallbestX, out int smallbestY);

            int roughX = (int)(smallbestX / scale);
            int roughY = (int)(smallbestY / scale);

            int searchRange = 300;
            int cropX = roughX - searchRange;
            int cropY = roughY - searchRange;
            if (cropX < 0)
            {
                cropX = 0;
            }
            if (cropY < 0)
            {
                cropY = 0;
            }
            int cropWidth = template.PixelWidth + searchRange * 2;
            int cropHeight = template.PixelHeight + searchRange * 2;
            if (cropX + cropWidth > source.PixelWidth)
            {
                cropWidth = source.PixelWidth - cropX;
            }
            if (cropY + cropHeight > source.PixelHeight)
            {
                cropHeight = source.PixelHeight - cropY;
            }
            Int32Rect cropRect = new Int32Rect(cropX, cropY, cropWidth, cropHeight);
            CroppedBitmap croppedSource = new CroppedBitmap(source, cropRect);

            byte[] cropGrey = MarkGrayPixels(croppedSource, out int cropWidth2, out int cropHeight2);
            byte[] templateGrey = MarkGrayPixels(template, out int templateWidth, out int templateHeight);
            FindBestMatch(cropGrey, cropWidth2, cropHeight2, templateGrey, templateWidth, templateHeight, out int localbestX, out int localbestY);
            int finalX = cropX + localbestX;
            int finalY = cropY + localbestY;
            ResultImage.Source = source;
            DrawMatchRectangle(finalX, finalY, template.PixelWidth, template.PixelHeight);
            stopwatch.Stop();
            ResultScrollViewer.ScrollToHorizontalOffset(finalX * zoomScale);
            ResultScrollViewer.ScrollToVerticalOffset(finalY * zoomScale);
            MessageBox.Show($"템플릿 매칭 시간: {stopwatch.ElapsedMilliseconds} ms\nrough: {roughX}, {roughY}\nfinal: {finalX}, {finalY}");

        }
        private byte[] MarkGrayPixels(BitmapSource source, out int width, out int height)
        {
            int bytesPerPixel = 4;
            FormatConvertedBitmap converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            width = converted.PixelWidth;
            height = converted.PixelHeight;
            int stride = width * bytesPerPixel;

            byte[] pixels = new byte[height * stride];
            byte[] gray = new byte[width * height];

            converted.CopyPixels(pixels, stride, 0);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * stride + x * 4;
                    int grayIndex = y * width + x;
                    byte B = pixels[index];
                    byte G = pixels[index + 1];
                    byte R = pixels[index + 2];
                    int grayValue = (B + G + R) / 3;
                    gray[grayIndex] = (byte)grayValue;
                }
            }
            return gray;
        }
        private BitmapSource MakeSmallBitmap(BitmapSource source, double scale)
        {
            ScaleTransform transform = new ScaleTransform(scale, scale);
            TransformedBitmap samllBitmap = new TransformedBitmap(source, transform);
            return samllBitmap;
        }
        private void FindBestMatch(byte[] originalGrey, int sourcewidth, int sourceheight, byte[] templateGrey, int templatewidth, int templateheight, out int bestX, out int bestY)
        {

            FindBestMatch_SSE2CPP(originalGrey, templateGrey, sourcewidth, sourceheight, templatewidth, templateheight, out bestX, out bestY);
        }
        private void CsharpFindBestMatch(byte[] originalGrey, int sourcewidth, int sourceheight, byte[] templateGrey, int templatewidth, int templateheight, out int bestX, out int bestY)
        {
            bestX = 0;
            bestY = 0;
            long bestscore = long.MaxValue;

            for (int y = 0; y <= sourceheight - templateheight; y++)
            {
                for (int x = 0; x <= sourcewidth - templatewidth; x++)
                {
                    long score = CalcurateTemplateSSD_SSE2(originalGrey, templateGrey, sourcewidth, templatewidth, templateheight, x, y, bestscore);
       
                    if (score < bestscore)
                    {
                        bestscore = score;
                        bestX = x;
                        bestY = y;
                    }
                }
            }
        }
        private void DrawMatchRectangle(int x, int y, int width, int height)
        {
            Rectangle rect = new Rectangle();
            rect.Width = width;
            rect.Height = height;
            rect.Stroke = Brushes.Red;
            rect.StrokeThickness = 3;
            rect.Fill = Brushes.Transparent;
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            ResultOverlay.Children.Add(rect);
        }
        private void Window_Closed(object sender, EventArgs e)
        {
            if (previewWindow != null && previewWindow.IsVisible)
            {
                previewWindow.Close();
                previewWindow = null;
            }
        }
    }
}








