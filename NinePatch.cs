using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
namespace NinePatchPreview
{
    /// <summary>
    /// C#中用于处理 Android 9-patch 图片的类
    /// Android 9-patch 图片在上边和左边定义了拉伸区域，
    /// 本类仅使用上边和左边的信息来处理。
    /// 
    /// 使用方法：
    /// 
    /// NinePatch ninePatch = new NinePatch(image); // 从9-patch图片image创建NinePatch对象
    /// Image newImage = ninePatch.ImageSizeOf(500, 500); // 获取拉伸到500x500大小的图片
    /// 
    /// </summary>
    public class NinePatch : IDisposable
    {
        /// <summary>
        /// 上下左右各1像素的9-patch图片
        /// </summary>
        private Image image;

        /// <summary>
        /// 返回去除上下左右1像素后的原始图片
        /// </summary>
        public Bitmap OriginalImage
        {
            get
            {
                using (Bitmap baseBitmap = new Bitmap(this.image))
                {
                    Rectangle rect = new Rectangle(1, 1, baseBitmap.Width - 2, baseBitmap.Height - 2);
                    Bitmap result = baseBitmap.Clone(rect, baseBitmap.PixelFormat);
                    return result;
                }
            }
        }

        /// <summary>
        /// 图像大小缓存
        /// </summary>
        private Dictionary<string, Image> cache;

        /// <summary>
        /// 上边界拉伸区域
        /// </summary>
        private List<int> topPatches;

        /// <summary>
        /// 左边界拉伸区域
        /// </summary>
        private List<int> leftPatches;

        private string _filePath;

        /// <summary>
        /// 构造函数，从给定图片路径创建NinePatch对象
        /// </summary>
        /// <param name="image">9-patch图片</param>
        public NinePatch(string filePath)
        {
            this.image = Image.FromFile(filePath);
            cache = new Dictionary<string, Image>();
            FindPatchRegion();
        }
        /// <summary>
        /// 构造函数，从给定图片Bitmap创建NinePatch对象
        /// </summary>
        /// <param name="image">9-patch图片</param>
        public NinePatch(Bitmap bitmap)
        {
            this.image = bitmap;
            cache = new Dictionary<string, Image>();
            FindPatchRegion();
        }
        /// <summary>
        /// 构造函数，从给定图片创建NinePatch对象
        /// </summary>
        /// <param name="image">9-patch图片</param>
        public NinePatch(Image image)
        {
            this.image = image;
            cache = new Dictionary<string, Image>();
            FindPatchRegion();
        }

        ///// <summary>
        ///// 清除图像缓存
        ///// </summary>
        //public void ClearCache()
        //{
        //    foreach (KeyValuePair<string, Image> pair in cache)
        //    {
        //        pair.Value.Dispose();
        //    }
        //    cache.Clear();
        //}
        /// <summary>
        /// 释放Bitmap资源
        /// </summary>
        public void Dispose()
        {
            foreach (KeyValuePair<string, Image> pair in cache)
            {
                pair.Value.Dispose();
            }
            cache.Clear();
            this.image?.Dispose(); // 如果Bitmap不为空，则释放资源
        }


        /// <summary>
        /// 根据指定宽度和高度创建拉伸后的图像对象
        /// </summary>
        /// <param name="w">目标宽度</param>
        /// <param name="h">目标高度</param>
        /// <returns>图像对象</returns>
        public Image ImageSizeOf(int w, int h)
        {
            /*
             * 如果已经存在相同尺寸的图像对象，则直接返回缓存中的对象
             */
            if (cache.ContainsKey(String.Format("{0}x{1}", w, h)))
            {
                return cache[String.Format("{0}x{1}", w, h)];
            }

            using (Bitmap src = this.OriginalImage)
            {
                int sourceWidth = src.Width;
                int sourceHeight = src.Height;
                int targetWidth = w;
                int targetHeight = h;

                // 如果目标尺寸小于源尺寸，则强制使用源尺寸
                targetWidth = System.Math.Max(sourceWidth, targetWidth);
                targetHeight = System.Math.Max(sourceHeight, targetHeight);
                if (sourceWidth == targetWidth && sourceHeight == targetHeight)
                {
                    return src;
                }

                // 准备源图像缓冲区
                BitmapData srcData = src.LockBits(
                    new Rectangle(0, 0, sourceWidth, sourceHeight),
                    ImageLockMode.ReadOnly,
                    src.PixelFormat
                );
                byte[] srcBuf = new byte[sourceWidth * sourceHeight * BYTES_PER_PIXEL];
                Marshal.Copy(srcData.Scan0, srcBuf, 0, srcBuf.Length);

                // 准备目标图像缓冲区
                Bitmap dst = new Bitmap(targetWidth, targetHeight);
                byte[] dstBuf = new byte[dst.Width * dst.Height * BYTES_PER_PIXEL];

                // 获取x和y轴的映射关系
                List<int> xMapping = XMapping(targetWidth - sourceWidth, targetWidth);
                List<int> yMapping = YMapping(targetHeight - sourceHeight, targetHeight);

                // 根据映射关系复制每个像素的值
                for (int y = 0; y < targetHeight; y++)
                {
                    int sourceY = yMapping[y];
                    for (int x = 0; x < targetWidth; x++)
                    {
                        int sourceX = xMapping[x];

                        for (int z = 0; z < BYTES_PER_PIXEL; z++)
                        {
                            dstBuf[y * targetWidth * BYTES_PER_PIXEL + x * BYTES_PER_PIXEL + z] =
                                srcBuf[sourceY * sourceWidth * BYTES_PER_PIXEL + sourceX * BYTES_PER_PIXEL + z];
                        }
                    }
                }

                // 将目标图像复制到Bitmap中
                BitmapData dstData = dst.LockBits(
                    new Rectangle(0, 0, dst.Width, dst.Height),
                    ImageLockMode.WriteOnly,
                    src.PixelFormat
                );
                IntPtr dstScan0 = dstData.Scan0;
                Marshal.Copy(dstBuf, 0, dstScan0, dstBuf.Length);

                src.UnlockBits(srcData);
                dst.UnlockBits(dstData);

                // 将大小和图像对象存入缓存
                cache.Add(String.Format("{0}x{1}", w, h), dst);

                return dst;
            }
        }

        /// <summary>
        /// 分析9-patch图片的上下左右1像素区域，确定拉伸区域
        /// </summary>
        private void FindPatchRegion()
        {
            topPatches = new List<int>();
            leftPatches = new List<int>();

            using (Bitmap src = new Bitmap(image))
            {
                BitmapData srcData = src.LockBits(
                    new Rectangle(0, 0, src.Width, src.Height),
                    ImageLockMode.ReadOnly,
                    src.PixelFormat
                );
                byte[] srcBuf = new byte[src.Width * src.Height * BYTES_PER_PIXEL];
                Marshal.Copy(srcData.Scan0, srcBuf, 0, srcBuf.Length);

                // 上边界
                for (int x = 1; x < srcData.Width - 1; x++)
                {
                    int index = x * BYTES_PER_PIXEL;
                    byte b = srcBuf[index];
                    byte g = srcBuf[index + 1];
                    byte r = srcBuf[index + 2];
                    byte alpha = srcBuf[index + 3];
                    if (r == 0 && g == 0 && b == 0 && alpha == 255)
                    {
                        topPatches.Add(x - 1);
                    }
                }

                // 左边界
                for (int y = 1; y < srcData.Height - 1; y++)
                {
                    int index = y * BYTES_PER_PIXEL * srcData.Width;
                    byte b = srcBuf[index];
                    byte g = srcBuf[index + 1];
                    byte r = srcBuf[index + 2];
                    byte alpha = srcBuf[index + 3];
                    if (r == 0 && g == 0 && b == 0 && alpha == 255)
                    {
                        leftPatches.Add(y - 1);
                    }
                }
            }
        }

        /// <summary>
        /// 每像素的字节数
        /// </summary>
        private const int BYTES_PER_PIXEL = 4;

        /// <summary>
        /// 获取x轴映射关系列表，用于目标图像的拉伸
        /// </summary>
        /// <param name="diffWidth">目标图像和原始图像的宽度差异</param>
        /// <param name="targetWidth">目标图像的宽度</param>
        /// <returns></returns>
        private List<int> XMapping(int diffWidth, int targetWidth)
        {
            List<int> result = new List<int>(targetWidth);
            int src = 0;
            int dst = 0;
            while (dst < targetWidth)
            {
                int foundIndex = topPatches.IndexOf(src);
                if (foundIndex != -1)
                {
                    int repeatCount = (diffWidth / topPatches.Count) + 1;
                    if (foundIndex < diffWidth % topPatches.Count)
                    {
                        repeatCount++;
                    }
                    for (int j = 0; j < repeatCount; j++)
                    {
                        result.Insert(dst++, src);
                    }
                }
                else
                {
                    result.Insert(dst++, src);
                }
                src++;
            }
            return result;
        }

        /// <summary>
        /// 获取y轴映射关系列表，用于目标图像的拉伸
        /// </summary>
        /// <param name="diffHeight">目标图像和原始图像的高度差异</param>
        /// <param name="targetHeight">目标图像的高度</param>
        /// <returns></returns>
        private List<int> YMapping(int diffHeight, int targetHeight)
        {
            List<int> result = new List<int>(targetHeight);
            int src = 0;
            int dst = 0;
            while (dst < targetHeight)
            {
                int foundIndex = leftPatches.IndexOf(src);
                if (foundIndex != -1)
                {
                    int repeatCount = (diffHeight / leftPatches.Count) + 1;
                    if (foundIndex < diffHeight % leftPatches.Count)
                    {
                        repeatCount++;
                    }
                    for (int j = 0; j < repeatCount; j++)
                    {
                        result.Insert(dst++, src);
                    }
                }
                else
                {
                    result.Insert(dst++, src);
                }
                src++;
            }
            return result;
        }
    }
}
