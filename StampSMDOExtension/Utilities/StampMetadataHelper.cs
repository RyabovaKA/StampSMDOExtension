using Ascon.Pilot.SDK;
using System;
using System.Threading.Tasks;

namespace StampSMDOExtension.Utilities
{
    public static class StampMetadataHelper
    {
        public static async Task<string> ResolveRefBookName(IObjectsRepository repository, IDataObject doc, string refAttrName, string nameAttr)
        {
            if (!doc.Attributes.TryGetValue(refAttrName, out var raw) || raw == null)
                return "";

            Guid refId = Guid.Empty;
            if (raw is Guid g) refId = g;
            else if (raw is string s && Guid.TryParse(s, out var pg)) refId = pg;
            else if (raw is string[] sa && sa.Length > 0 && Guid.TryParse(sa[0], out var pg2)) refId = pg2;
            else if (raw is Guid[] ga && ga.Length > 0) refId = ga[0];

            if (refId == Guid.Empty) return raw.ToString();

            try
            {
                var refObj = await StampObjectLoader.Load(repository, refId);
                if (refObj == null) return "";
                return refObj.Attributes.TryGetValue(nameAttr, out var nameVal)
                    ? nameVal?.ToString() ?? ""
                    : "";
            }
            catch
            {
                return "";
            }
        }

        public static string GetAttributeString(IDataObject obj, string attrName)
        {
            return obj.Attributes.TryGetValue(attrName, out var val) ? val?.ToString() : null;
        }

        public static string GetAttributeDateString(IDataObject obj, string attrName)
        {
            if (obj.Attributes.TryGetValue(attrName, out var val) && val is DateTime dt)
                return dt.ToString("dd.MM.yyyy");
            return null;
        }
    }
}