using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;

namespace Stendahls.Sc.BlobStorage.Cms.Extensions
{
    public static class TemplateExtensions
    {
        private static readonly ConcurrentDictionary<Guid, ISet<Guid>> TemplateInheritanceCache =
            new ConcurrentDictionary<Guid, ISet<Guid>>();

        public static bool InheritsTemplate(this TemplateItem template, string templatePathOrGuid)
        {
            if (string.IsNullOrWhiteSpace(templatePathOrGuid))
                return false;

            if (ID.IsID(templatePathOrGuid))
            {
                return InheritsTemplate(template, new ID(templatePathOrGuid));
            }

            try
            {
                return InheritsTemplate(template, template.Database.GetTemplate(templatePathOrGuid).ID);
            }
            catch (Exception ex)
            {
                Log.Error($"Unable to find template '{templatePathOrGuid}': {ex.Message}", ex, typeof(Sitecore.Data.Templates.Template));
                return false;
            }
        }

        public static bool InheritsTemplate(this TemplateItem template, ID templateId)
        {
            return InheritsTemplate(template, templateId.Guid);
        }

        public static bool InheritsTemplate(this TemplateItem template, Guid guid)
        {
            if (template.ID.Guid.Equals(guid))
                return true;

            var bag = TemplateInheritanceCache.GetOrAdd(template.ID.Guid, 
                templateGuid => new HashSet<Guid>(GetInheritedTemplates(template)));
            return bag.Contains(guid);
        }

        public static IEnumerable<Guid> GetInheritedTemplates(TemplateItem template)
        {
            foreach (var baseTemplate in template.BaseTemplates)
            {
                yield return baseTemplate.ID.Guid;
                foreach (var inheritedTemplateGuid in GetInheritedTemplates(baseTemplate))
                {
                    yield return inheritedTemplateGuid;
                }
            }
        }
    }
}