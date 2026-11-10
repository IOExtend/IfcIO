using IfcIO.Core;
using IfcIO.Geometry;
using IfcIO.Model;
using IfcIO.Schema;

namespace IfcIO.Validation;

/// <summary>
/// Comprehensive validator for IFC object models and STEP physical file structures.
/// </summary>
public static class IfcValidator
{
    private const string BuildingSmartGuidChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

    /// <summary>
    /// Validates an <see cref="IfcModel"/> against buildingSMART MVD and geometric integrity rules.
    /// </summary>
    public static IfcValidationResult Validate(IfcModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var issues = new List<IfcValidationIssue>();

        // 1. Project & Schema rules
        if (string.IsNullOrWhiteSpace(model.ProjectName))
        {
            issues.Add(new IfcValidationIssue
            {
                RuleId = "IFC_PROJECT_NAME",
                Severity = IfcIssueSeverity.Error,
                Message = "ProjectName must not be null or whitespace."
            });
        }

        if (model.Schema != IfcSchema.Ifc2x3 && model.Schema != IfcSchema.Ifc4)
        {
            issues.Add(new IfcValidationIssue
            {
                RuleId = "IFC_SCHEMA_VERSION",
                Severity = IfcIssueSeverity.Warning,
                Message = $"Model specifies non-standard or custom schema '{model.Schema}'."
            });
        }

        // 2. GlobalId uniqueness and format
        var seenGuids = new Dictionary<string, string>(StringComparer.Ordinal);

        void CheckGlobalId(string? globalId, string entityDescription)
        {
            if (string.IsNullOrEmpty(globalId))
            {
                return;
            }

            if (globalId.Length != 22)
            {
                issues.Add(new IfcValidationIssue
                {
                    RuleId = "IFC_GLOBAL_ID_LENGTH",
                    Severity = IfcIssueSeverity.Error,
                    Message = $"GlobalId '{globalId}' has invalid length ({globalId.Length}). buildingSMART standard requires exactly 22 characters.",
                    EntityName = entityDescription
                });
            }
            else
            {
                bool validChars = true;
                foreach (char c in globalId)
                {
                    if (!BuildingSmartGuidChars.Contains(c))
                    {
                        validChars = false;
                        break;
                    }
                }

                if (!validChars)
                {
                    issues.Add(new IfcValidationIssue
                    {
                        RuleId = "IFC_GLOBAL_ID_CHARS",
                        Severity = IfcIssueSeverity.Error,
                        Message = $"GlobalId '{globalId}' contains characters outside the buildingSMART base64 character set.",
                        EntityName = entityDescription
                    });
                }
            }

            if (seenGuids.TryGetValue(globalId, out var previous))
            {
                issues.Add(new IfcValidationIssue
                {
                    RuleId = "IFC_GLOBAL_ID_UNIQUE",
                    Severity = IfcIssueSeverity.Error,
                    Message = $"Duplicate GlobalId '{globalId}' shared between '{previous}' and '{entityDescription}'.",
                    EntityName = entityDescription
                });
            }
            else
            {
                seenGuids[globalId] = entityDescription;
            }
        }

        // 3. Storey & Spatial Structure
        if (model.Storeys.Count == 0)
        {
            issues.Add(new IfcValidationIssue
            {
                RuleId = "IFC_SPATIAL_STOREYS",
                Severity = IfcIssueSeverity.Warning,
                Message = "Model has zero IfcBuildingStoreys defined. Elements are not partitioned across building levels."
            });
        }

        for (int s = 0; s < model.Storeys.Count; s++)
        {
            var storey = model.Storeys[s];
            string storeyDesc = $"Storey '{storey.Name}'";

            CheckGlobalId(storey.GlobalId, storeyDesc);

            if (!double.IsFinite(storey.Elevation))
            {
                issues.Add(new IfcValidationIssue
                {
                    RuleId = "IFC_STOREY_ELEVATION",
                    Severity = IfcIssueSeverity.Error,
                    Message = $"Storey elevation is non-finite: {storey.Elevation}.",
                    EntityName = storeyDesc
                });
            }
        }

        // 4. Elements & Geometric Integrity
        var allElements = model.AllElements.ToList();

        for (int e = 0; e < allElements.Count; e++)
        {
            var element = allElements[e];
            string elemDesc = $"{element.IfcType} '{element.Name}' (ID {element.Id})";

            CheckGlobalId(element.GlobalId, elemDesc);

            if (!element.IfcType.StartsWith("IFC", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new IfcValidationIssue
                {
                    RuleId = "IFC_TYPE_NAME_PREFIX",
                    Severity = IfcIssueSeverity.Warning,
                    Message = $"IfcType '{element.IfcType}' does not start with standard 'IFC' prefix.",
                    EntityName = elemDesc
                });
            }

            // Geometry validation
            if (element.Mesh is not null)
            {
                ValidateMesh(element.Mesh, elemDesc, issues);
            }
            else if (element.Solid is not null)
            {
                ValidateSolid(element.Solid, elemDesc, issues);
            }
            else if (element.RevolvedSolid is not null)
            {
                ValidateRevolvedSolid(element.RevolvedSolid, elemDesc, issues);
            }

            // Openings validation
            if (element.Openings.Count > 0)
            {
                foreach (var opening in element.Openings)
                {
                    string opDesc = $"Opening '{opening.Name}' (ID {opening.Id}) in {elemDesc}";
                    CheckGlobalId(opening.GlobalId, opDesc);
                    if (opening.Mesh is not null)
                    {
                        ValidateMesh(opening.Mesh, opDesc, issues);
                    }
                    else if (opening.Solid is not null)
                    {
                        ValidateSolid(opening.Solid, opDesc, issues);
                    }
                }
            }

            // Property set validation
            ValidatePropertySets(element.PropertySets, elemDesc, issues);

            // Quantity set validation
            ValidateQuantitySets(element.QuantitySets, elemDesc, issues);
        }

        // 5. Type Products
        for (int t = 0; t < model.TypeProducts.Count; t++)
        {
            var typeProd = model.TypeProducts[t];
            string typeDesc = $"TypeProduct '{typeProd.Name}'";

            CheckGlobalId(typeProd.GlobalId, typeDesc);
            ValidatePropertySets(typeProd.PropertySets, typeDesc, issues);
        }

        return new IfcValidationResult { Issues = issues };
    }

    private static void ValidateSolid(IfcExtrudedAreaSolid solid, string entityName, List<IfcValidationIssue> issues)
    {
        if (solid.Depth <= 0 || !double.IsFinite(solid.Depth))
        {
            issues.Add(new IfcValidationIssue
            {
                RuleId = "IFC_SOLID_DEPTH",
                Severity = IfcIssueSeverity.Error,
                Message = $"Extruded solid depth must be a positive finite number (actual: {solid.Depth}).",
                EntityName = entityName
            });
        }

        ValidateProfile(solid.Profile, entityName, issues);
    }

    private static void ValidateRevolvedSolid(IfcRevolvedAreaSolid solid, string entityName, List<IfcValidationIssue> issues)
    {
        if (solid.Angle <= 0 || !double.IsFinite(solid.Angle) || solid.Angle > 2.0 * Math.PI + 1e-4)
        {
            issues.Add(new IfcValidationIssue
            {
                RuleId = "IFC_REVOLVED_ANGLE",
                Severity = IfcIssueSeverity.Error,
                Message = $"Revolved solid angle must be in range (0, 2pi] (actual: {solid.Angle}).",
                EntityName = entityName
            });
        }

        if (solid.Axis.Axis.Length < 1e-6)
        {
            issues.Add(new IfcValidationIssue
            {
                RuleId = "IFC_REVOLVED_AXIS",
                Severity = IfcIssueSeverity.Error,
                Message = "Revolved solid axis direction must not be zero vector.",
                EntityName = entityName
            });
        }

        ValidateProfile(solid.Profile, entityName, issues);
    }

    private static void ValidateProfile(IfcProfileDef? profile, string entityName, List<IfcValidationIssue> issues)
    {
        if (profile is null)
        {
            issues.Add(new IfcValidationIssue
            {
                RuleId = "IFC_SOLID_PROFILE",
                Severity = IfcIssueSeverity.Error,
                Message = "Solid has null profile.",
                EntityName = entityName
            });
        }
        else if (profile is IfcRectangleProfileDef rect)
        {
            if (rect.XDim <= 0 || !double.IsFinite(rect.XDim) || rect.YDim <= 0 || !double.IsFinite(rect.YDim))
            {
                issues.Add(new IfcValidationIssue
                {
                    RuleId = "IFC_PROFILE_DIMENSIONS",
                    Severity = IfcIssueSeverity.Error,
                    Message = $"Rectangle profile dimensions must be positive finite numbers (XDim: {rect.XDim}, YDim: {rect.YDim}).",
                    EntityName = entityName
                });
            }
        }
        else if (profile is IfcCircleProfileDef circ)
        {
            if (circ.Radius <= 0 || !double.IsFinite(circ.Radius))
            {
                issues.Add(new IfcValidationIssue
                {
                    RuleId = "IFC_PROFILE_DIMENSIONS",
                    Severity = IfcIssueSeverity.Error,
                    Message = $"Circle profile radius must be a positive finite number (actual: {circ.Radius}).",
                    EntityName = entityName
                });
            }
        }
    }

    private static void ValidateQuantitySets(List<IfcQuantitySet> qsets, string entityName, List<IfcValidationIssue> issues)
    {
        foreach (var qset in qsets)
        {
            string qsetDesc = $"QuantitySet '{qset.Name}' in {entityName}";
            if (string.IsNullOrWhiteSpace(qset.Name))
            {
                issues.Add(new IfcValidationIssue
                {
                    RuleId = "IFC_QUANTITY_NAME",
                    Severity = IfcIssueSeverity.Error,
                    Message = "Quantity set name must not be whitespace.",
                    EntityName = entityName
                });
            }

            foreach (var q in qset.Quantities)
            {
                if (string.IsNullOrWhiteSpace(q.Name))
                {
                    issues.Add(new IfcValidationIssue
                    {
                        RuleId = "IFC_QUANTITY_NAME",
                        Severity = IfcIssueSeverity.Error,
                        Message = $"Physical quantity in {qsetDesc} has null or whitespace name.",
                        EntityName = entityName
                    });
                }

                double val = q switch
                {
                    IfcQuantityLength ql => ql.LengthValue,
                    IfcQuantityArea qa => qa.AreaValue,
                    IfcQuantityVolume qv => qv.VolumeValue,
                    IfcQuantityCount qc => qc.CountValue,
                    IfcQuantityWeight qw => qw.WeightValue,
                    _ => 0.0,
                };

                if (val < 0 || !double.IsFinite(val))
                {
                    issues.Add(new IfcValidationIssue
                    {
                        RuleId = "IFC_QUANTITY_POSITIVE",
                        Severity = IfcIssueSeverity.Error,
                        Message = $"Physical quantity '{q.Name}' value must be non-negative finite number (actual: {val}).",
                        EntityName = qsetDesc
                    });
                }
            }
        }
    }

    private static void ValidateMesh(IfcMesh mesh, string entityName, List<IfcValidationIssue> issues)
    {
        if (mesh.Vertices.Count < 3)
        {
            issues.Add(new IfcValidationIssue
            {
                RuleId = "IFC_GEOMETRY_VERTEX_COUNT",
                Severity = IfcIssueSeverity.Error,
                Message = $"Mesh '{mesh.Name}' has fewer than 3 vertices ({mesh.Vertices.Count}).",
                EntityName = entityName
            });
            return;
        }

        for (int v = 0; v < mesh.Vertices.Count; v++)
        {
            var pt = mesh.Vertices[v];
            if (!double.IsFinite(pt.X) || !double.IsFinite(pt.Y) || !double.IsFinite(pt.Z))
            {
                issues.Add(new IfcValidationIssue
                {
                    RuleId = "IFC_GEOMETRY_VERTEX_COORDINATES",
                    Severity = IfcIssueSeverity.Error,
                    Message = $"Mesh vertex #{v} has non-finite coordinates: {pt}.",
                    EntityName = entityName
                });
                break;
            }
        }

        if (mesh.Indices.Count == 0 || mesh.Indices.Count % 3 != 0)
        {
            issues.Add(new IfcValidationIssue
            {
                RuleId = "IFC_GEOMETRY_INDEX_COUNT",
                Severity = IfcIssueSeverity.Error,
                Message = $"Mesh triangle index count ({mesh.Indices.Count}) is not a multiple of 3.",
                EntityName = entityName
            });
            return;
        }

        int degenerateCount = 0;
        for (int i = 0; i <= mesh.Indices.Count - 3; i += 3)
        {
            int i0 = mesh.Indices[i];
            int i1 = mesh.Indices[i + 1];
            int i2 = mesh.Indices[i + 2];

            if (i0 < 0 || i0 >= mesh.Vertices.Count ||
                i1 < 0 || i1 >= mesh.Vertices.Count ||
                i2 < 0 || i2 >= mesh.Vertices.Count)
            {
                issues.Add(new IfcValidationIssue
                {
                    RuleId = "IFC_GEOMETRY_INDEX_OUT_OF_BOUNDS",
                    Severity = IfcIssueSeverity.Error,
                    Message = $"Triangle face ({i0}, {i1}, {i2}) references indices outside vertex array (size {mesh.Vertices.Count}).",
                    EntityName = entityName
                });
                break;
            }

            if (i0 == i1 || i1 == i2 || i0 == i2)
            {
                degenerateCount++;
            }
            else
            {
                var v0 = mesh.Vertices[i0];
                var v1 = mesh.Vertices[i1];
                var v2 = mesh.Vertices[i2];
                var normal = IfcVector3.Cross(v1 - v0, v2 - v0);
                if (normal.Length < 1e-12)
                {
                    degenerateCount++;
                }
            }
        }

        if (degenerateCount > 0)
        {
            issues.Add(new IfcValidationIssue
            {
                RuleId = "IFC_GEOMETRY_DEGENERATE_FACES",
                Severity = IfcIssueSeverity.Warning,
                Message = $"Mesh '{mesh.Name}' contains {degenerateCount} degenerate (zero-area or duplicate-index) triangle faces.",
                EntityName = entityName
            });
        }
    }

    private static void ValidatePropertySets(List<IfcPropertySet> psets, string entityName, List<IfcValidationIssue> issues)
    {
        for (int p = 0; p < psets.Count; p++)
        {
            var pset = psets[p];
            if (string.IsNullOrWhiteSpace(pset.Name))
            {
                issues.Add(new IfcValidationIssue
                {
                    RuleId = "IFC_PSET_NAME_EMPTY",
                    Severity = IfcIssueSeverity.Warning,
                    Message = "Property set has empty or whitespace name.",
                    EntityName = entityName
                });
            }

            var propNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int pr = 0; pr < pset.Properties.Count; pr++)
            {
                var prop = pset.Properties[pr];
                if (string.IsNullOrWhiteSpace(prop.Name))
                {
                    issues.Add(new IfcValidationIssue
                    {
                        RuleId = "IFC_PROPERTY_NAME_EMPTY",
                        Severity = IfcIssueSeverity.Warning,
                        Message = $"Property set '{pset.Name}' contains a property with empty name.",
                        EntityName = entityName
                    });
                }
                else if (!propNames.Add(prop.Name))
                {
                    issues.Add(new IfcValidationIssue
                    {
                        RuleId = "IFC_PROPERTY_NAME_DUPLICATE",
                        Severity = IfcIssueSeverity.Warning,
                        Message = $"Property set '{pset.Name}' contains duplicate property '{prop.Name}'.",
                        EntityName = entityName
                    });
                }
            }
        }
    }

    /// <summary>
    /// Validates raw ISO 10303-21 STEP physical file structure.
    /// </summary>
    public static IfcValidationResult ValidateStep(StepFile step)
    {
        ArgumentNullException.ThrowIfNull(step);

        var issues = new List<IfcValidationIssue>();

        if (string.IsNullOrEmpty(step.Schema))
        {
            issues.Add(new IfcValidationIssue
            {
                RuleId = "STEP_SCHEMA_MISSING",
                Severity = IfcIssueSeverity.Warning,
                Message = "STEP file header lacks FILE_SCHEMA declaration."
            });
        }

        var projects = step.OfType("IFCPROJECT").ToList();
        if (projects.Count == 0)
        {
            issues.Add(new IfcValidationIssue
            {
                RuleId = "STEP_PROJECT_MISSING",
                Severity = IfcIssueSeverity.Error,
                Message = "Model contains no IFCPROJECT entity."
            });
        }
        else if (projects.Count > 1)
        {
            issues.Add(new IfcValidationIssue
            {
                RuleId = "STEP_PROJECT_MULTIPLE",
                Severity = IfcIssueSeverity.Warning,
                Message = $"Model contains {projects.Count} IFCPROJECT entities. Exactly one is expected."
            });
        }

        return new IfcValidationResult { Issues = issues };
    }
}
