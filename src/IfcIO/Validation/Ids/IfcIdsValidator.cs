using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using IfcIO.Model;

namespace IfcIO.Validation.Ids;

/// <summary>
/// High-performance buildingSMART Information Delivery Specification (IDS 1.0) compliance validator
/// for IFC models, directly mirroring the IfcOpenShell / IfcTester audit pipeline in pure managed .NET.
/// </summary>
public static class IfcIdsValidator
{
    private static readonly XNamespace IdsNs = "http://standards.buildingsmart.org/IDS";
    private static readonly XNamespace XsiNs = "http://www.w3.org/2001/XMLSchema-instance";

    /// <summary>
    /// Validates an <see cref="IfcModel"/> against an IDS 1.0 specification file (.ids).
    /// </summary>
    public static IfcIdsValidationReport ValidateFile(IfcModel model, string idsPath)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(idsPath);

        using var fs = File.OpenRead(idsPath);
        return Validate(model, fs);
    }

    /// <summary>
    /// Validates an <see cref="IfcModel"/> against an IDS XML stream.
    /// </summary>
    public static IfcIdsValidationReport Validate(IfcModel model, Stream idsXmlStream)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(idsXmlStream);

        var doc = XDocument.Load(idsXmlStream);
        return Validate(model, doc);
    }

    /// <summary>
    /// Validates an <see cref="IfcModel"/> against a loaded IDS <see cref="XDocument"/>.
    /// </summary>
    public static IfcIdsValidationReport Validate(IfcModel model, XDocument idsDoc)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(idsDoc);

        var infoElem = idsDoc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("info", StringComparison.OrdinalIgnoreCase));
        string title = (string?)infoElem?.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("title", StringComparison.OrdinalIgnoreCase)) ?? "IDS Compliance Audit";
        string? author = (string?)infoElem?.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("author", StringComparison.OrdinalIgnoreCase));
        string? version = (string?)infoElem?.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("version", StringComparison.OrdinalIgnoreCase)) ?? "1.0";

        var report = new IfcIdsValidationReport
        {
            Title = title,
            Author = author,
            IdsVersion = version,
        };

        var specElements = idsDoc.Descendants().Where(e => e.Name.LocalName.Equals("specification", StringComparison.OrdinalIgnoreCase)).ToList();
        report.TotalSpecifications = specElements.Count;

        var allElements = model.AllElements.ToList();

        foreach (var specElem in specElements)
        {
            string specName = (string?)specElem.Attribute("name") ?? "Unnamed Specification";
            var applicabilityElem = specElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("applicability", StringComparison.OrdinalIgnoreCase));
            var requirementsElem = specElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("requirements", StringComparison.OrdinalIgnoreCase));

            // 1. Identify applicable elements
            var applicableElements = FindApplicableElements(allElements, applicabilityElem);
            report.TotalApplicableElements += applicableElements.Count;

            if (applicableElements.Count == 0 || requirementsElem is null)
            {
                report.PassedSpecifications++;
                continue;
            }

            // 2. Evaluate requirements against applicable elements
            int failureCountBefore = report.Failures.Count;

            foreach (var elem in applicableElements)
            {
                EvaluateRequirements(elem, specName, requirementsElem, report.Failures);
            }

            bool specPassed = report.Failures.Count == failureCountBefore;
            if (specPassed)
            {
                report.PassedSpecifications++;
            }
            else
            {
                report.FailedSpecifications++;
            }
        }

        return report;
    }

    private static List<IfcElement> FindApplicableElements(List<IfcElement> elements, XElement? applicabilityElem)
    {
        if (applicabilityElem is null) return elements;

        var entityElems = applicabilityElem.Elements().Where(e => e.Name.LocalName.Equals("entity", StringComparison.OrdinalIgnoreCase)).ToList();
        var propElems = applicabilityElem.Elements().Where(e => e.Name.LocalName.Equals("property", StringComparison.OrdinalIgnoreCase)).ToList();
        var matElems = applicabilityElem.Elements().Where(e => e.Name.LocalName.Equals("material", StringComparison.OrdinalIgnoreCase)).ToList();

        var result = new List<IfcElement>();

        foreach (var elem in elements)
        {
            // If entity filters specified, element must match at least one entity filter
            if (entityElems.Count > 0)
            {
                bool matchesEntity = false;
                foreach (var ee in entityElems)
                {
                    string? nameVal = GetFacetValue(ee.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase)));
                    if (string.IsNullOrEmpty(nameVal) || nameVal == "*" ||
                        elem.IfcType.Equals(nameVal, StringComparison.OrdinalIgnoreCase) ||
                        elem.IfcType.StartsWith(nameVal, StringComparison.OrdinalIgnoreCase))
                    {
                        matchesEntity = true;
                        break;
                    }
                }
                if (!matchesEntity) continue;
            }

            // If property filters specified, element must match all property filters
            bool matchesProps = true;
            foreach (var pe in propElems)
            {
                string? pset = GetFacetValue(pe.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("propertySet", StringComparison.OrdinalIgnoreCase)));
                string? baseName = GetFacetValue(pe.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("baseName", StringComparison.OrdinalIgnoreCase)));
                string? expectedVal = GetFacetValue(pe.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("value", StringComparison.OrdinalIgnoreCase)));

                if (!HasProperty(elem, pset, baseName, expectedVal))
                {
                    matchesProps = false;
                    break;
                }
            }
            if (!matchesProps) continue;

            // If material filter specified
            if (matElems.Count > 0)
            {
                bool matchesMat = false;
                foreach (var me in matElems)
                {
                    string? matVal = GetFacetValue(me.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("value", StringComparison.OrdinalIgnoreCase)));
                    if (elem.Material is not null && (string.IsNullOrEmpty(matVal) || elem.Material.Name.Contains(matVal, StringComparison.OrdinalIgnoreCase)))
                    {
                        matchesMat = true;
                        break;
                    }
                }
                if (!matchesMat) continue;
            }

            result.Add(elem);
        }

        return result;
    }

    private static void EvaluateRequirements(IfcElement elem, string specName, XElement requirementsElem, List<IfcIdsFailure> failures)
    {
        // 1. Property requirements
        var propReqs = requirementsElem.Elements().Where(e => e.Name.LocalName.Equals("property", StringComparison.OrdinalIgnoreCase));
        foreach (var pr in propReqs)
        {
            string minOccurs = (string?)pr.Attribute("minOccurs") ?? "1";
            if (minOccurs == "0") continue; // Optional

            string? pset = GetFacetValue(pr.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("propertySet", StringComparison.OrdinalIgnoreCase)));
            string? baseName = GetFacetValue(pr.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("baseName", StringComparison.OrdinalIgnoreCase)));
            string? expectedVal = GetFacetValue(pr.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("value", StringComparison.OrdinalIgnoreCase)));

            if (string.IsNullOrEmpty(baseName)) continue;

            if (!HasProperty(elem, pset, baseName, expectedVal, out string? actualVal, out bool propertyFound))
            {
                string msg = !propertyFound
                    ? $"Missing required property '{(!string.IsNullOrEmpty(pset) ? pset + "." : "")}{baseName}'"
                    : $"Property '{(!string.IsNullOrEmpty(pset) ? pset + "." : "")}{baseName}' has value '{actualVal}', expected '{expectedVal}'";

                failures.Add(new IfcIdsFailure
                {
                    SpecificationName = specName,
                    ElementId = elem.Id,
                    ElementGuid = elem.GlobalId,
                    ElementName = elem.Name,
                    ElementType = elem.IfcType,
                    FacetType = "Property",
                    Message = msg,
                });
            }
        }

        // 2. Material requirements
        var matReqs = requirementsElem.Elements().Where(e => e.Name.LocalName.Equals("material", StringComparison.OrdinalIgnoreCase));
        foreach (var mr in matReqs)
        {
            string minOccurs = (string?)mr.Attribute("minOccurs") ?? "1";
            if (minOccurs == "0") continue;

            string? expectedMat = GetFacetValue(mr.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("value", StringComparison.OrdinalIgnoreCase)));
            if (elem.Material is null)
            {
                failures.Add(new IfcIdsFailure
                {
                    SpecificationName = specName,
                    ElementId = elem.Id,
                    ElementGuid = elem.GlobalId,
                    ElementName = elem.Name,
                    ElementType = elem.IfcType,
                    FacetType = "Material",
                    Message = $"Element lacks required material assignment (expected '{expectedMat ?? "Any"}')",
                });
            }
            else if (!string.IsNullOrEmpty(expectedMat) && !elem.Material.Name.Contains(expectedMat, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add(new IfcIdsFailure
                {
                    SpecificationName = specName,
                    ElementId = elem.Id,
                    ElementGuid = elem.GlobalId,
                    ElementName = elem.Name,
                    ElementType = elem.IfcType,
                    FacetType = "Material",
                    Message = $"Material '{elem.Material.Name}' does not match required '{expectedMat}'",
                });
            }
        }

        // 3. Attribute requirements (Name, Description, Tag)
        var attrReqs = requirementsElem.Elements().Where(e => e.Name.LocalName.Equals("attribute", StringComparison.OrdinalIgnoreCase));
        foreach (var ar in attrReqs)
        {
            string minOccurs = (string?)ar.Attribute("minOccurs") ?? "1";
            if (minOccurs == "0") continue;

            string? attrName = GetFacetValue(ar.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase)));
            string? expectedAttr = GetFacetValue(ar.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("value", StringComparison.OrdinalIgnoreCase)));

            if (string.IsNullOrEmpty(attrName)) continue;

            string? actualAttr = attrName.ToLowerInvariant() switch
            {
                "name" => elem.Name,
                "description" => elem.TypeProduct?.Description,
                "tag" => elem.Tag,
                "objecttype" => elem.TypeProduct?.Name,
                _ => null
            };

            if (string.IsNullOrEmpty(actualAttr))
            {
                failures.Add(new IfcIdsFailure
                {
                    SpecificationName = specName,
                    ElementId = elem.Id,
                    ElementGuid = elem.GlobalId,
                    ElementName = elem.Name,
                    ElementType = elem.IfcType,
                    FacetType = "Attribute",
                    Message = $"Missing required attribute '{attrName}'",
                });
            }
            else if (!string.IsNullOrEmpty(expectedAttr) && !actualAttr.Equals(expectedAttr, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add(new IfcIdsFailure
                {
                    SpecificationName = specName,
                    ElementId = elem.Id,
                    ElementGuid = elem.GlobalId,
                    ElementName = elem.Name,
                    ElementType = elem.IfcType,
                    FacetType = "Attribute",
                    Message = $"Attribute '{attrName}' has value '{actualAttr}', expected '{expectedAttr}'",
                });
            }
        }
    }

    private static bool HasProperty(IfcElement elem, string? pset, string? baseName, string? expectedVal) =>
        HasProperty(elem, pset, baseName, expectedVal, out _, out _);

    private static bool HasProperty(IfcElement elem, string? pset, string? baseName, string? expectedVal, out string? actualVal, out bool propertyFound)
    {
        actualVal = null;
        propertyFound = false;
        if (string.IsNullOrEmpty(baseName)) return true;

        foreach (var ps in elem.PropertySets)
        {
            if (!string.IsNullOrEmpty(pset) && !ps.Name.Equals(pset, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? v = ps.GetValue(baseName);
            if (v is not null)
            {
                propertyFound = true;
                actualVal = v;

                if (string.IsNullOrEmpty(expectedVal)) return true; // Just existence required

                if (bool.TryParse(expectedVal, out bool eb))
                {
                    bool actualBool = v.Equals(".T.", StringComparison.OrdinalIgnoreCase) ||
                                      v.Equals("TRUE", StringComparison.OrdinalIgnoreCase);
                    return eb == actualBool;
                }

                if (double.TryParse(expectedVal, NumberStyles.Float, CultureInfo.InvariantCulture, out double ed) &&
                    double.TryParse(actualVal, NumberStyles.Float, CultureInfo.InvariantCulture, out double ad))
                {
                    return Math.Abs(ed - ad) < 1e-6;
                }

                return string.Equals(actualVal, expectedVal, StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    private static string? GetFacetValue(XElement? parent)
    {
        if (parent is null) return null;
        var simpleVal = parent.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("simpleValue", StringComparison.OrdinalIgnoreCase));
        if (simpleVal is not null) return simpleVal.Value.Trim();
        return parent.Value.Trim();
    }
}
