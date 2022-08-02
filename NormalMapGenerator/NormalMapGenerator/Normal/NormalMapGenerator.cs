/********************************************************************************
 *   作者：朱济松                                                               *
 *   创建时间：2022/8/2 1:38:42                                                 *
 *                                                                              *
 *   源代码: https://github.com/jisong-zhu/NormalMapGenerator.Net               *
 ********************************************************************************/
using ImageMagick;
using NormalMapGenerator.Math;
using System;
using System.Collections.Generic;
using System.IO;

namespace NormalMapGenerator.Normal
{
    /// <summary>
    /// 生成法向贴图数据
    /// </summary>
    public class NormalMapGenerator
    {
        /// <summary>
        /// 根据纹理图数据生成法向贴图
        /// </summary>
        /// <param name="filePath">纹理图片路径</param>
        /// <param name="kernel">算法选择</param>
        /// <param name="strength">法向强度</param>
        /// <param name="invert">是否反向</param>
        /// <param name="keepLargeDetail">保留细节</param>
        /// <param name="largeDetailScale">保留的细节比例：0~1</param>
        /// <returns></returns>
        public byte[] Generate(string filePath, KernelTypes kernel, float strength, bool invert, bool keepLargeDetail, float largeDetailScale)
        {
            var bytes = File.ReadAllBytes(filePath);
            return Generate(bytes, kernel, strength, invert, keepLargeDetail, largeDetailScale);
        }
        /// <summary>
        /// 根据纹理图数据生成法向贴图
        /// </summary>
        /// <param name="buffer">byte类型的文件数据</param>
        /// <param name="kernel">算法选择</param>
        /// <param name="strength">法向强度</param>
        /// <param name="invert">是否反向</param>
        /// <param name="keepLargeDetail">保留细节</param>
        /// <param name="largeDetailScale">保留的细节比例：0~1</param>
        /// <returns></returns>
        public byte[] Generate(byte[] buffer, KernelTypes kernel, float strength, bool invert, bool keepLargeDetail, float largeDetailScale)
        {
            using var mImage = new MagickImage(buffer);
            using var norImage = new MagickImage(MagickColors.Transparent, mImage.Width, mImage.Height);
            var sourceWidth = mImage.Width;
            var sourceHeight = mImage.Height;
            CalculateNormalmap(mImage, norImage, kernel, strength, invert);
            if (keepLargeDetail)
            {
                int largeDetailMapWidth = (int)((double)mImage.Width * largeDetailScale);
                int largeDetailMapHeight = (int)((double)mImage.Height * largeDetailScale);
                using var largeNormalImage = new MagickImage(MagickColors.Transparent, largeDetailMapWidth, largeDetailMapHeight);
                mImage.Scale(largeDetailMapWidth, largeDetailMapHeight);

                CalculateNormalmap(mImage, largeNormalImage, kernel, strength, invert);
                largeNormalImage.Scale(sourceWidth, sourceHeight);
                var largeNornalPixels = largeNormalImage.GetPixels();

                var pixels = norImage.GetPixels();
                foreach (var pixel in pixels)
                {
                    var color1 = pixel.ToColor();
                    var color2 = largeNornalPixels.GetPixel(pixel.X, pixel.Y).ToColor();

                    var r = BlendSoftLight(color1.R, color2.R);
                    var g = BlendSoftLight(color1.G, color2.G);
                    var b = BlendSoftLight(color1.B, color2.B);
                    pixels.SetPixel(pixel.X, pixel.Y, new byte[] { r, g, b, 255 });
                }
            }
            norImage.Format = MagickFormat.Jpg;
            norImage.Blur(5, 1);
            return norImage.ToByteArray();
        }

        public void CalculateNormalmap(MagickImage mImage, MagickImage norImage, KernelTypes kernel, float strength, bool invert)
        {
            var intensityMap = new Dictionary<string, double>();
            var normalStrength = 1 / strength;

            var pixels = mImage.GetPixels();
            var norPixels = norImage.GetPixels();

            foreach (var pixel in pixels)
            {
                var color = pixel.ToColor();
                var intensity = ((double)(color.R + color.G + color.B)) / (255 * 3);
                intensityMap.Add($"{pixel.X},{pixel.Y}", invert ? 1.0f - intensity : intensity);
            }
            foreach (var pixel in pixels)
            {
                var x = pixel.X;
                var y = pixel.Y;
                var topLeft = intensityMap.GetValueOrDefault($"{HandleEdges(x - 1, mImage.Width)},{HandleEdges(y - 1, mImage.Height)}");
                var top = intensityMap.GetValueOrDefault($"{HandleEdges(x - 1, mImage.Width)},{HandleEdges(y, mImage.Height)}");
                var topRight = intensityMap.GetValueOrDefault($"{HandleEdges(x - 1, mImage.Width)},{HandleEdges(y + 1, mImage.Height)}");
                var right = intensityMap.GetValueOrDefault($"{HandleEdges(x, mImage.Width)},{HandleEdges(y + 1, mImage.Height)}");
                var bottomRight = intensityMap.GetValueOrDefault($"{HandleEdges(x + 1, mImage.Width)},{HandleEdges(y + 1, mImage.Height)}");
                var bottom = intensityMap.GetValueOrDefault($"{HandleEdges(x + 1, mImage.Width)},{HandleEdges(y, mImage.Height)}");
                var bottomLeft = intensityMap.GetValueOrDefault($"{HandleEdges(x + 1, mImage.Width)},{HandleEdges(y - 1, mImage.Height)}");
                var left = intensityMap.GetValueOrDefault($"{HandleEdges(x, mImage.Width)},{HandleEdges(y - 1, mImage.Height)}");

                var convolution_kernel = new double[3, 3] {
                    { topLeft, top, topRight},
                    { left, 0.0, right},
                    { bottomLeft, bottom, bottomRight}
                };
                var normal = new Vector3d(0, 0, 0);
                if (kernel == KernelTypes.SOBEL)
                {
                    // sobel
                    normal = Sobel(convolution_kernel, normalStrength);
                }
                else if (kernel == KernelTypes.PREWITT)
                {
                    // Prewitt
                    normal = Prewitt(convolution_kernel, normalStrength);
                }
                var newColor = new byte[] { MapComponent(normal.x), MapComponent(normal.y), MapComponent(normal.z), 255 };

                norPixels.SetPixel(x, y, newColor);
            }

        }


        private Vector3d Sobel(double[,] convolution_kernel, double strengthInv)
        {
            var top_side = convolution_kernel[0, 0] + 2.0 * convolution_kernel[0, 1] + convolution_kernel[0, 2];
            var bottom_side = convolution_kernel[2, 0] + 2.0 * convolution_kernel[2, 1] + convolution_kernel[2, 2];
            var right_side = convolution_kernel[0, 2] + 2.0 * convolution_kernel[1, 2] + convolution_kernel[2, 2];
            var left_side = convolution_kernel[0, 0] + 2.0 * convolution_kernel[1, 0] + convolution_kernel[2, 0];

            var dY = right_side - left_side;
            var dX = bottom_side - top_side;
            var dZ = strengthInv;

            return new Vector3d(dX, dY, dZ).Normalized;
        }

        private Vector3d Prewitt(double[,] convolution_kernel, double strengthInv)
        {
            var top_side = convolution_kernel[0, 0] + convolution_kernel[0, 1] + convolution_kernel[0, 2];
            var bottom_side = convolution_kernel[2, 0] + convolution_kernel[2, 1] + convolution_kernel[2, 2];
            var right_side = convolution_kernel[0, 2] + convolution_kernel[1, 2] + convolution_kernel[2, 2];
            var left_side = convolution_kernel[0, 0] + convolution_kernel[1, 0] + convolution_kernel[2, 0];

            var dY = right_side - left_side;
            var dX = top_side - bottom_side;
            var dZ = strengthInv;

            return new Vector3d(dX, dY, dZ).Normalized;
        }

        /// <summary>
        /// 处理数值边界
        /// </summary>
        /// <param name="iterator"></param>
        /// <param name="maxValue"></param>
        /// <returns></returns>
        private int HandleEdges(int iterator, int maxValue)
        {
            if (iterator >= maxValue)
            {
                return maxValue - 1;
            }
            else if (iterator < 0)
            {
                return 0;
            }
            else
            {
                return iterator;
            }
        }

        /// <summary>
        /// 将0~1的数值转换为0~255的颜色值
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private byte MapComponent(double value)
        {
            return (byte)((value + 1.0) * (255.0 / 2.0));
        }
        /// <summary>
        /// uses a similar algorithm like "soft light" in PS, 
        /// see http://www.michael-kreil.de/algorithmen/photoshop-layer-blending-equations/index.php
        /// </summary>
        /// <param name="color1"></param>
        /// <param name="color2"></param>
        /// <returns></returns>
        private byte BlendSoftLight(int color1, int color2)
        {
            var a = color1;
            var b = color2;

            if (2.0f * b < 255.0f)
            {
                return (byte)(((a + 127.5f) * b) / 255.0f);
            }
            else
            {
                return (byte)(255.0f - (((382.5f - a) * (255.0f - b)) / 255.0f));
            }
        }

    }
}
