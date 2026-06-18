using System;
using System.IO;

namespace StampSMDOExtension.UI
{
    internal static class IconLoader
    {
        public static byte[] GetIcon(string resourceName)
        {
            using (var stream = typeof(IconLoader).Assembly.GetManifestResourceStream(
                       $"StampSMDOExtension.Icons.{resourceName}"))
            {
                if (stream == null)
                    throw new InvalidOperationException($"Embedded icon not found: Icons\\{resourceName}");
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }
    }
}
