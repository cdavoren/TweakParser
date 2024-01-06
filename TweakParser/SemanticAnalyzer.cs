using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.Serialization;
using static TweakParser.SemanticNode;

namespace TweakParser
{
    public class SemanticAnalyzerException : Exception
    {
        public SemanticAnalyzerException(string message) : base(message) { }
    }

    public class FlattenRules
    {
        public List<string> StringKeyAllowedPackages { get; }

        public FlattenRules(List<string>? stringKeyAllowedPackages = null)
        {
            StringKeyAllowedPackages = new();
            if (stringKeyAllowedPackages is not null)
            {
                StringKeyAllowedPackages.AddRange(stringKeyAllowedPackages);
            }
        }
    }

    public class PackageInformation
    {
        public string Name { get; set; }
        public List<RecordSemanticNode> Records { get; set; }
        public List<FlatSemanticNode> Flats { get; set; }

        public PackageInformation(string name)
        {
            Name = name;
            Records = [];
            Flats = [];
        }
    }

    public class GlobalContext
    {
        public List<PackageInformation> Packages { get; set; }
        public Dictionary<string, SemanticNode> RootNodes { get; set; }
        public List<RecordSemanticNode> FreeRecords { get; set; }
        public List<FlatSemanticNode> FreeFlats { get; set; }

        protected Dictionary<string, RecordSemanticNode> ResolvedRecords { get; set; }

        public GlobalContext()
        {
            Packages = [];
            RootNodes = [];
            FreeRecords = [];
            FreeFlats = [];
            ResolvedRecords = [];
        }

        public PackageInformation GetPackage(string packageName)
        {
            var packageNode = Packages.FirstOrDefault(x => x.Name == packageName);
            if (packageNode is not null)
            {
                return packageNode;
            }
            else
            {
                packageNode = new PackageInformation(packageName);
                Packages.Add(packageNode);
                return packageNode;
            }
        }

        public PackageInformation? ResolveDeclarationPackage(string? name, PackageContextSemanticNode packageContext)
        {
            if (name is null)
            {
                return null;
            }
            if (name.Contains('.'))
            {
                var identifiers = name.Split('.');
                if (identifiers.Length > 2)
                {
                    throw new SemanticAnalyzerException(string.Format("Invalid name encountered attempting to resolve package: {0}", name));
                }
                return GetPackage(identifiers[0]);
            }
            else if (packageContext.Name is null)
            { 
                return null; 
            }
            else
            {
                return GetPackage(packageContext.Name);
            }
        }

        public string FullyQualifiedName(RecordSemanticNode recordNode)
        {
            var recordBaseName = recordNode.BaseName();
            if (recordBaseName is null)
            {
                throw new SemanticAnalyzerException(string.Format("Anonymous/inline records do not have a fully qualified name"));
            }

            if (FreeRecords.Contains(recordNode))
            {
                return recordBaseName;
            }
            else
            {
                foreach (var package in Packages)
                {
                    if (package.Records.Contains(recordNode))
                    {
                        return package.Name + '.' + recordBaseName;
                    }
                }
            }
            throw new SemanticAnalyzerException(string.Format("Record with name {0} is not registered in the global context", recordNode.Name));
        }

        public string FullyQualifiedName(string recordName, PackageContextSemanticNode packageContext)
        {
            if (recordName.Contains('.'))
            {
                return recordName;
            }
            else
            {
                List<string?> potentialPackages =
                [
                    .. packageContext.UsingPackageReferences,
                    packageContext.Name,
                    // TODO: HACK --- RTDB seems to be implicit import in some packages
                    "RTDB",
                ];
                foreach (var packageName in potentialPackages)
                {
                    if (packageName is null)
                    {
                        continue;
                    }

                    var package = Packages.FirstOrDefault(package => package.Name == packageName);
                    if (package is null)
                    {
                        continue;
                    }
                    //foreach (var record in package.Records.OrderBy(r => r.Name))
                    //{
                    //    Console.WriteLine(record.Name);
                    //}
                    if (package.Records.Count(record => record.BaseName() == recordName) > 0)
                    {
                        return packageName + "." + recordName;
                    }
                }
                if (FreeRecords.Count(record => record.Name == recordName) > 0)
                {
                    return recordName;
                }
                throw new SemanticAnalyzerException(string.Format("Unable to resolve record name {0} in the current context", recordName));
            }
        }

        public RecordSemanticNode? ResolveRecordType(string? type, PackageContextSemanticNode? packageContext)
        {
            if (type is null)
            {
                return null;
            }

            if (type.Contains('.'))
            {
                // Console.WriteLine(type);
                var identifiers = type.Split('.');
                if (identifiers.Length > 2)
                {
                    throw new SemanticAnalyzerException(string.Format("Invalid type name encountered attempting to resolve record type: {0}", type));
                }
                var packageInfo = GetPackage(identifiers[0]);
                var record = packageInfo.Records.FirstOrDefault((x => x.Name == identifiers[1]));
                if (record is null)
                {
                    return null;
                }
                else
                {
                    return record;
                }
            }
            else if (packageContext is null)
            {
                return null;
            }
            else
            {
                var possiblePackages = new List<string>();
                possiblePackages.Add("RTDB");
                possiblePackages.AddRange(packageContext.UsingPackageReferences);
                if (packageContext.Name is not null)
                {
                    possiblePackages.Add(packageContext.Name);
                }
                // Base packages occasionally forget to put RTDB in using list -- HACK --
                foreach (var packageName in possiblePackages)
                {
                    var packageInfo = GetPackage(packageName);
                    foreach (var recordNode in packageInfo.Records)
                    {
                        var baseName = recordNode.BaseName();
                        if (baseName is null)
                        {
                            // Have we registered anonymous inlines? Possibly?
                            continue;
                        }
                        if (baseName.Equals(type))
                        {
                            return recordNode;
                        }
                    }
                }

                return null;
            }
        }

        public FlatSemanticNode? FindRootInheritedFlat(RecordSemanticNode recordNode, string name, PackageContextSemanticNode packageContext)
        {
            // Purpose of this method is to avoid using existing resolutions because there are circular references -> cannot rely on parent class being fully resolved as yet
            // That's OK, because all we need is a reference to the root flat type
            // For this reason, we can do our own resolving based on the extant foreign key string if necessary
            // Console.WriteLine(string.Format("[ FindRootInheritedFlat ] Called with {0} ({1}) to find type for '{2}'", recordNode.Name, recordNode.InheritedFrom, name));
            var matchingFlat = recordNode.HasFlatWithName(name);
            if (matchingFlat is null || (matchingFlat.FlatType == FlatTypes.Unresolved || matchingFlat.FlatType == FlatTypes.InlineRecord))
            {
                var parentRecord = ResolveRecordType(recordNode.InheritedFrom, recordNode.PackageContext);
                if (parentRecord is null)
                {
                    // Console.WriteLine("   - returning null");
                    return null;
                }
                return FindRootInheritedFlat(parentRecord, name, parentRecord.PackageContext);
            }
            // Console.WriteLine(string.Format("   - returning {0} ({1})", matchingFlat.Name, matchingFlat.FlatType));
            return matchingFlat;
        }
    }

    public class SemanticAnalyzer
    {
        protected List<string> topLevelPackages = new();
        protected Dictionary<string, SyntaxNode> topLevelNodes = new(); // string key is filename
        protected GlobalContext GlobalContext { get; set; }

        protected StreamWriter? analyzerLog;

        public SemanticAnalyzer()
        {
            this.GlobalContext = new();
        }

        protected void RegisterRecord(RecordSemanticNode recordNode, PackageContextSemanticNode packageContext)
        {
            if (recordNode.Name is null)
            {
                // inline records do not require registration
                return;
            }
            else if (recordNode.Name.Contains('.'))
            {
                //if (recordNode.Name.Contains("AIActionSmartComposite"))
                //{
                //    throw new SemanticAnalyzerException("DEBUGGING EXCEPTION");
                //}
                // Must be a full-length specifier
                var specifiers = recordNode.Name.Split('.');
                if (specifiers.Length > 2)
                {
                    throw new SemanticAnalyzerException(string.Format("Unable to process record name with more than one namespace separator: {0}", recordNode.Name));
                }
                var packageName = specifiers[0];
                var recordName = specifiers[1];
                analyzerLog!.WriteLine(string.Format("Registering record {0} with package {1}", recordName, packageName));
                var package = GlobalContext.GetPackage(packageName);
                package.Records.Add(recordNode);
                recordNode.Name = specifiers[1];
                //if (package.Name == "player" && recordNode.Name == "player")
                //{
                //    throw new SemanticAnalyzerException("DEBUGGING EXCEPTION");
                //}

            }
            else if (packageContext.Name is not null)
            {
                var packageName = packageContext.Name;
                var package = GlobalContext.GetPackage(packageName);
                package.Records.Add(recordNode);
            }
            else
            {
                GlobalContext.FreeRecords.Add(recordNode);
            }
        }

        protected void RegisterTopFlat(FlatSemanticNode flatNode, PackageContextSemanticNode packageContext)
        {
            if (flatNode.Name.Contains('.'))
            {
                var specifiers = flatNode.Name.Split(".");
                if (specifiers.Length > 2)
                {
                    throw new SemanticAnalyzerException(string.Format("Unable to process flat name with more than one namespace separator: {0}", flatNode.Name));
                }
                var packageName = specifiers[0];
                var flatNodeName = specifiers[1];
                flatNode.Name = flatNodeName;

                var package = GlobalContext.GetPackage(packageName);
                package.Flats.Add(flatNode);
            }
            else
            {
                if (packageContext.Name is null)
                {
                    throw new SemanticAnalyzerException(string.Format("Flat found without both package context and complete specifier: {0}", flatNode.Name));
                }
                var package = GlobalContext.GetPackage(packageContext.Name);
                package.Flats.Add(flatNode);
            }
        }

        public SemanticNode Analyze(SyntaxNode rootNode, string filename)
        {
            analyzerLog = new StreamWriter("analyzer.log", false);

            analyzerLog.WriteLine("--- ANALYZER START ---");
            var semanticRootNode = new SemanticNode();

            var numPackageDelcarations = rootNode.GetChildren().Count(x => x.Type == "package");
            analyzerLog.WriteLine("Number of package definitions: " + numPackageDelcarations);

            var packageContext = new PackageContextSemanticNode(null);

            if (numPackageDelcarations > 1)
            {
                throw new SemanticAnalyzerException("Root node has more than one package declaration");
            }
            else if (numPackageDelcarations == 1)
            {
                packageContext.Name = rootNode.GetChildren().First(x => x.Type == "package").Value;
            }

            packageContext.UsingPackageReferences = new();
            var usingNode = rootNode.GetChildren().FirstOrDefault(x => x.Type == "using-declaration");

            if (usingNode is not null)
            {
                foreach (var packageReferenceNode in usingNode.GetChildren().Where(x => x.Type == "package-name"))
                {
                    if (packageReferenceNode.Value is not null)
                    {
                        packageContext.UsingPackageReferences.Add(packageReferenceNode.Value);
                    }
                }
            }
            semanticRootNode.AddChild(packageContext);

            var recordChildren = rootNode.GetChildren().Where(x => x.Type.Equals("record"));
            foreach (var recordChild in recordChildren)
            {
                var recordNode = AnalyzeRecord(recordChild, packageContext);
                RegisterRecord(recordNode, packageContext);
                semanticRootNode.AddChild(recordNode);
            }

            foreach (var flatNode in rootNode.GetChildren().Where(x => x.Type.Equals("flat-definition")))
            {
                var flatSemanticNode = AnalyzeFlatDefinition(flatNode, packageContext);
                if (flatSemanticNode is null)
                {
                    continue;
                }
                semanticRootNode.AddChild(flatSemanticNode);
                RegisterTopFlat(flatSemanticNode, packageContext);
            }

            analyzerLog.WriteLine("--- ANALYZER END ---");
            semanticRootNode.Print(string.Empty, analyzerLog);
            analyzerLog.WriteLine(string.Format("Finished: {0}", DateTime.Now.ToString()));
            analyzerLog.Flush();
            analyzerLog.Close();
            GlobalContext.RootNodes.Add(filename, semanticRootNode);
            return semanticRootNode;
        }

        protected string StaticPackageName(string? name)
        {
            if (name == null)
            {
                return string.Empty;
            }
            if (!name.Contains('.'))
            {
                return string.Empty;
            }
            else
            {
                var identifiers = name.Split('.');
                if (identifiers.Length > 2)
                {
                    return string.Empty;
                }
                return identifiers[0];
            }
        }

        protected string? FullySpecifiedName(string? name, PackageContextSemanticNode packageContext)
        {
            if (name is null)
            {
                return null;
            }
            else if (name.Contains('.'))
            {
                return name;
            }
            else if (packageContext.Name is not null)
            {
                return packageContext.Name + '.' + name;
            }
            else
            {
                return name;
            }
        }

        public RecordSemanticNode AnalyzeRecord(SyntaxNode recordSyntaxNode, PackageContextSemanticNode packageContext, bool isTopLevel = false)
        {
            RecordSemanticNode returnNode;

            string? name = recordSyntaxNode.Value; // if null, is inline

            analyzerLog!.WriteLine(string.Format("Analyzing record {0}", name));

            var inheritanceNodes = recordSyntaxNode.GetChildren().Where(x => x.Type.Equals("record-inheritance")).ToList();

            if (inheritanceNodes is null || inheritanceNodes.Count == 0)
            {
                // Inline record or group -> treat as same for now
                if (name is null && !isTopLevel)
                {
                    throw new SemanticAnalyzerException("Unable to resolve an inline record with no specified type");
                }
                returnNode = new RecordSemanticNode(FullySpecifiedName(name, packageContext), null);
                if (!(isTopLevel && packageContext.Name is not null && packageContext.Name.Equals("RTDB")) || StaticPackageName(name).Equals("RTDB"))
                {
                    // Top-level nodes in this Base schema type - special case
                    returnNode.IsGroup = true;
                }
            }
            else if (inheritanceNodes.Count() > 1)
            {
                throw new SemanticAnalyzerException(string.Format("Record {0} may not inherit more than once ({1}, {2})", name, inheritanceNodes[0], inheritanceNodes[1]));
            }
            else
            {
                returnNode = new RecordSemanticNode(name, inheritanceNodes[0].Value);
            }

            var flatListNode = recordSyntaxNode.GetChildren().FirstOrDefault(x => x.Type.Equals("flat-list"));
            if (flatListNode is not null)
            {
                returnNode.AddChildRange(AnalyzeFlatList(flatListNode, packageContext));
            }
            returnNode.PackageContext = packageContext;
            return returnNode;
        }

        protected FlatTypes StringToFlatType(string? flatTypeString)
        {
            if (flatTypeString == null)
            {
                return FlatTypes.Unresolved;
            }

            if (flatTypeString.Equals("foreignkey") || flatTypeString.Equals("value-foreignkey"))
            {
                return FlatTypes.ForeignKey;
            }
            else if (flatTypeString.Equals("string") || flatTypeString.Equals("value-string"))
            {
                return FlatTypes.String;
            }
            else if (flatTypeString.Equals("int") || flatTypeString.Equals("value-number-integer"))
            {
                return FlatTypes.Integer;
            }
            else if (flatTypeString.Equals("float") || flatTypeString.Equals("value-number-float"))
            {
                return FlatTypes.Float;
            }
            else if (flatTypeString.Equals("bool") || flatTypeString.Equals("value-boolean"))
            {
                return FlatTypes.Boolean;
            }
            else if (flatTypeString.Equals("CName"))
            {
                return FlatTypes.CName;
            }
            else if (flatTypeString.Equals("Vector2") || flatTypeString.Equals("value-vector"))
            {
                return FlatTypes.Vector2;
            }
            else if (flatTypeString.Equals("Vector3") || flatTypeString.Equals("value-vector3"))
            {
                return FlatTypes.Vector3;
            }
            else if (flatTypeString.Equals("Vector4") || flatTypeString.Equals("value-vector4"))
            {
                return FlatTypes.Vector4;
            }
            else if (flatTypeString.Equals("LocKey"))
            {
                return FlatTypes.LocKey;
            }
            else if (flatTypeString.Equals("ResRef"))
            {
                return FlatTypes.ResRef;
            }
            else if (flatTypeString.Equals("EulerAngles"))
            {
                return FlatTypes.EulerAngles;
            }
            else if (flatTypeString.Equals("Quaternion"))
            {
                return FlatTypes.Quaternion;
            }
            else if (flatTypeString.Equals("value-identifier"))
            {
                return FlatTypes.Identifier;
            }
            else if (flatTypeString.Equals("value-inline"))
            {
                return FlatTypes.InlineRecord;
            }
            else
            {
                throw new SemanticAnalyzerException(string.Format("Unrecognised type {0}", flatTypeString));
            }
        }

        protected FlatSemanticNode? AnalyzeFlatDefinition(SyntaxNode flatDefinitionSyntaxNode, PackageContextSemanticNode packageContext)
        {
            if (flatDefinitionSyntaxNode.Type == "special-tag")
            {
                // We are ignoring special tags for now
                return null;
            }

            FlatTypes flatType = FlatTypes.Unresolved;
            bool isArray = false;
            string? foreignKeyName = null;

            var flatTypeNode = flatDefinitionSyntaxNode.GetChildren().FirstOrDefault(x => x.Type.Equals("flat-type"));
            if (flatTypeNode is not null)
            {
                if (flatTypeNode.GetValueOrEmpty().Equals("array"))
                {
                    isArray = true;
                    var subTypeNode = flatTypeNode.GetChildren().FirstOrDefault(z => z.Type.Equals("flat-type"));
                    if (subTypeNode is null)
                    {
                        throw new SemanticAnalyzerException("Array type declared but no subtype found");
                    }
                    flatType = StringToFlatType(subTypeNode.Value);
                    if (flatType == FlatTypes.ForeignKey)
                    {
                        // flatDefinitionSyntaxNode.Print(string.Empty);
                        foreignKeyName = subTypeNode.GetChildren().First(y => y.Type.Equals("foreignkey-type")).Value;
                    }
                }
                else
                {
                    flatType = StringToFlatType(flatTypeNode.Value);
                    if (flatType == FlatTypes.ForeignKey)
                    {
                        foreignKeyName = flatTypeNode.GetChildren().First(y => y.Type.Equals("foreignkey-type")).Value;
                    }
                }
            }

            string name;
            var nameNode = flatDefinitionSyntaxNode.GetChildren().FirstOrDefault(x => x.Type.Equals("name"));
            if (nameNode is not null && nameNode.Value is not null)
            {
                name = nameNode.Value;
            }
            else
            {
                throw new SemanticAnalyzerException("Flat has no name");
            }

            analyzerLog!.WriteLine(string.Format("     Analyzing flat with name {0}", name));

            var operatorType = OperatorTypes.Assign;
            var operatorNode = flatDefinitionSyntaxNode.GetChildren().FirstOrDefault(x => x.Type.Equals("operation"));
            if (operatorNode is null)
            {
                throw new SemanticAnalyzerException("Flat definition lacks an operator");
            }
            var operatorString = operatorNode.Value;
            if (operatorString is null)
            {
                throw new SemanticAnalyzerException("Flat definition has null operator node value");
            }
            else if (operatorString.Equals("append"))
            {
                operatorType = OperatorTypes.Append;
            }
            else if (operatorString.Equals("assign"))
            {
                operatorType = OperatorTypes.Assign;
            }
            else
            {
                throw new SemanticAnalyzerException(string.Format("Flat definition has unknown operator type {0}", operatorString));
            }

            var valueSyntaxNode = flatDefinitionSyntaxNode.GetChildren().FirstOrDefault(x => x.Type.StartsWith("value-"));
            if (valueSyntaxNode is null)
            {
                throw new SemanticAnalyzerException("Flat must have a value");
            }

            var valueSemanticNode = AnalyzeValue(valueSyntaxNode, packageContext);

            var flatNode = new FlatSemanticNode(name, flatType, isArray, valueSemanticNode, operatorType);
            if (flatType == FlatTypes.ForeignKey)
            {
                flatNode.ForeignKeyName = foreignKeyName;
            }
            //if (flatNode.Name.Equals("advertisements") && flatDefinitionSyntaxNode.GetParent().GetParent().Value == "AdvertisementGroup")
            //{
            //    flatDefinitionSyntaxNode.Print(string.Empty);
            //    Console.WriteLine("");
            //}
            flatNode.PackageContext = packageContext;
            return flatNode;
        }

        protected ValueSemanticNode AnalyzeValue(SyntaxNode valueSyntaxNode, PackageContextSemanticNode packageContext)
        {
            var valueIsArray = false;
            var valueFlatType = FlatTypes.Unresolved;
            string? foreignKeyName = null;

            if (valueSyntaxNode is null)
            {
                throw new SemanticAnalyzerException("Flat has no value");
            }
            else if (valueSyntaxNode.Type.Equals("value-list"))
            {
                valueIsArray = true;
                var valueChildrenNodes = valueSyntaxNode.GetChildren();

                var typesFound = new HashSet<FlatTypes>();
                var mixedReferenceTypes = new HashSet<FlatTypes>() { FlatTypes.String, FlatTypes.ForeignKey, FlatTypes.InlineRecord };
                var mixedNumericalTypes = new HashSet<FlatTypes>() { FlatTypes.Integer, FlatTypes.Float };
                foreach (var valueChild in valueChildrenNodes)
                {
                    typesFound.Add(StringToFlatType(valueChild.Type));
                }
                if (typesFound.Count > 1)
                {
                    // Special case where a list of foreign keys can contain inline inherited records and string references - this is OK, worry about it during resolution phase
                    if (!(typesFound.Count > 1 && (mixedReferenceTypes.IsSupersetOf(typesFound) || mixedNumericalTypes.IsSupersetOf(typesFound))))
                    {
                        throw new SemanticAnalyzerException("Found flat values of more than one type");
                    }
                    else if (typesFound.Contains(FlatTypes.ForeignKey))
                    {
                        valueFlatType = FlatTypes.ForeignKey;
                        foreignKeyName = valueChildrenNodes.First(x => x.Type == "value-foreignkey").Value;
                    }
                    else if (typesFound.Contains(FlatTypes.Float))
                    {
                        valueFlatType = FlatTypes.Float;
                    }
                    else
                    {
                        valueFlatType = FlatTypes.ForeignKey;
                        foreignKeyName = null;
                    }
                }
                else if (typesFound.Count == 0)
                {
                    valueFlatType = FlatTypes.Unresolved;
                }
                else
                {
                    valueFlatType = typesFound.First();
                }
            }
            else
            {
                valueFlatType = StringToFlatType(valueSyntaxNode.Type);
            }

            var valueSemanticNode = new ValueSemanticNode(valueFlatType, valueIsArray, new List<SyntaxNode>() { valueSyntaxNode });
            valueSemanticNode.ForeignKeyName = foreignKeyName;

            if (!valueIsArray && (valueFlatType == FlatTypes.InlineRecord || valueFlatType == FlatTypes.ForeignKey))
            {
                var flatListNode = valueSyntaxNode.GetChildren().FirstOrDefault(x => x.Type.Equals("flat-list"));
                if (flatListNode is not null)
                {
                    valueSemanticNode.AddChildRange(AnalyzeFlatList(flatListNode, packageContext));
                }
                else
                {
                    throw new SemanticAnalyzerException("Inline or foreign key value type has no associated flat definition list");
                }

                if (valueFlatType == FlatTypes.ForeignKey)
                {
                    valueSemanticNode.ForeignKeyName = valueSyntaxNode.Value;
                }
            }
            else if (valueIsArray)
            {
                analyzerLog!.WriteLine("    Parsing subnodes for array values in value ");
                foreach (var flatListValueSyntaxNode in valueSyntaxNode.GetChildren())
                {
                    var listValueSemanticNode = AnalyzeValue(flatListValueSyntaxNode, packageContext);
                    if (listValueSemanticNode is null)
                    {
                        throw new SemanticAnalyzerException(string.Format("Invalid array value node: {0} - {1}", flatListValueSyntaxNode.Type, flatListValueSyntaxNode.Value));
                    }
                    valueSemanticNode.AddChild(listValueSemanticNode);
                }
            }

            return valueSemanticNode;
        }

        protected List<FlatSemanticNode> AnalyzeFlatList(SyntaxNode flatListSyntaxNode, PackageContextSemanticNode packageContext)
        {
            var flatList = new List<FlatSemanticNode>();
            foreach (var flatSyntaxNode in flatListSyntaxNode.GetChildren())
            {
                var flatSemanticNode = AnalyzeFlatDefinition(flatSyntaxNode, packageContext);
                if (flatSemanticNode is not null)
                {
                    flatList.Add(flatSemanticNode);
                }
            }
            return flatList;
        }

        public void ResolveReferences()
        {
            analyzerLog = new StreamWriter("analyzer.log", true);
            analyzerLog.WriteLine("--- RESOLVER START ---");
            foreach (var rootNodeTuple in GlobalContext.RootNodes)
            {
                var rootNode = rootNodeTuple.Value;
                var packageContext = rootNode.GetRootPackageContext();
                analyzerLog.WriteLine(string.Format("Commencing resolution of node tree from file {0}", rootNodeTuple.Key));
                analyzerLog.Flush();

                var resolutionStack = new Stack<SemanticNode>(); // For circular references
                foreach (var flatNodeBase in rootNode.GetChildren().Where(x => x.Type == SemanticNodeTypes.FlatDefinition))
                {
                    var flatNode = flatNodeBase as FlatSemanticNode;
                    if (flatNode is null)
                    {
                        throw new SemanticAnalyzerException(string.Format("Incorrect subclass in flat definition types: {0}", flatNodeBase.GetType().ToString()));
                    }
                    if (flatNode.FlatType == FlatTypes.Unresolved)
                    {
                        throw new SemanticAnalyzerException(string.Format("Unresolved type on top-level flat: {0} - what to do?", flatNode.Name));
                    }
                    ResolveReferencesFlat(flatNode, resolutionStack, null);
                }
                foreach (var recordNodeBase in rootNode.GetChildren().Where(x => x.Type == SemanticNodeTypes.RecordDefinition))
                {
                    var recordNode = recordNodeBase as RecordSemanticNode;
                    if (recordNode is null)
                    {
                        throw new SemanticAnalyzerException(string.Format("Incorrect subclass in record definition types: {0}", recordNodeBase.GetType().ToString()));
                    }
                    ResolveReferencesRecord(recordNode, resolutionStack);
                }
            }
            analyzerLog.WriteLine("--- RESOLVER END ---");
            analyzerLog.Flush();
            analyzerLog.Close();
        }

        protected FlatSemanticNode ResolveReferencesFlat(FlatSemanticNode flatNode, Stack<SemanticNode> resolutionStack, FlatSemanticNode? parentRecordFlat = null)
        {
            if (resolutionStack.Contains(flatNode))
            {
                return flatNode;
            }
            resolutionStack.Push(flatNode);
            // Assumes parent record type has already been resolved
            // ALL INLINE RECORDS ARE UNRESOLVED FOREIGN KEYS (I hope)
            var packageContext = flatNode.PackageContext;

            if (parentRecordFlat is not null)
            {
                var parentIsArrayOrImplicit = parentRecordFlat is not null && (parentRecordFlat.IsArray || parentRecordFlat.OperatorType == OperatorTypes.Append);

                if ((parentIsArrayOrImplicit && !flatNode.Value.IsArray && flatNode.OperatorType == OperatorTypes.Assign) || (!parentIsArrayOrImplicit && flatNode.Value.IsArray))
                {
                    throw new SemanticAnalyzerException(string.Format("Cannot reconcile array and operator types - parent {0} {1} {2} {3}, child {4} {5} {6} {7}", parentRecordFlat!.Name, parentRecordFlat.FlatType, parentRecordFlat.IsArray, parentRecordFlat.OperatorType, flatNode.Name, flatNode.FlatType, flatNode.IsArray, flatNode.OperatorType));
                }
                if ((flatNode.FlatType != FlatTypes.Unresolved && flatNode.FlatType != FlatTypes.InlineRecord) && (flatNode.FlatType != parentRecordFlat!.FlatType))
                {
                    throw new SemanticAnalyzerException(string.Format("Cannot redeclare inherited types ({0} declared as {1}, parent {2} is {3}", flatNode.Name, flatNode.FlatType, parentRecordFlat.Name, parentRecordFlat.FlatType));
                }

                flatNode.FlatType = parentRecordFlat!.FlatType;
                flatNode.IsArray = parentRecordFlat.IsArray;

                flatNode.ForeignKeyName = parentRecordFlat.ForeignKeyName;
                flatNode.ForeignKeyRef = parentRecordFlat.ForeignKeyRef;
            }
            else
            {
                if (flatNode.FlatType == FlatTypes.Unresolved || flatNode.FlatType == FlatTypes.InlineRecord)
                {
                    throw new SemanticAnalyzerException(string.Format("Non-inherited flats must have type explicitly declared: {0}", flatNode.Name));
                }
            }

            RecordSemanticNode? foreignKeyReference;
            if (flatNode.FlatType == FlatTypes.ForeignKey && parentRecordFlat is null)
            {
                if (flatNode.ForeignKeyName is null)
                {
                    throw new SemanticAnalyzerException(string.Format("Flat {0} declared as foreign key but has no type name", flatNode.Name));
                }

                foreignKeyReference = GlobalContext.ResolveRecordType(flatNode.ForeignKeyName, packageContext);
                if (foreignKeyReference is null)
                {
                    throw new SemanticAnalyzerException(string.Format("Unable to resolve string foreign key reference {0} in current context", flatNode.Value.StringValue()));
                }
                ResolveReferencesRecord(foreignKeyReference, resolutionStack);
                flatNode.ForeignKeyRef = foreignKeyReference;
                // flatNode.Value.ForeignKeyRef = foreignKeyReference;
            }

            if (flatNode.FlatType == FlatTypes.ForeignKey && flatNode.ForeignKeyName is null)
            {
                throw new SemanticAnalyzerException(string.Format("Flat {0} declared as foreign key without type", flatNode.Name));
            }

            if (flatNode.ForeignKeyName is not null && flatNode.ForeignKeyRef is null)
            {
                throw new SemanticAnalyzerException(string.Format("Unresolved flat type {0} for flat {1}", flatNode.ForeignKeyName, flatNode.Name));
            }

            if (!flatNode.IsArray)
            {
                // Not an array
                if (flatNode.Value.ValueType == FlatTypes.ForeignKey || flatNode.Value.ValueType == FlatTypes.InlineRecord)
                {
                    flatNode.Value.ValueType = FlatTypes.ForeignKey;
                    if (flatNode.Value.ForeignKeyName is null || flatNode.Value.ForeignKeyName.Equals(flatNode.ForeignKeyName))
                    {
                        flatNode.Value.ForeignKeyName = flatNode.ForeignKeyName;
                        flatNode.Value.ForeignKeyRef = flatNode.ForeignKeyRef;
                    }
                    else
                    {
                        var valueForeignKeyRef = GlobalContext.ResolveRecordType(flatNode.Value.ForeignKeyName, packageContext);
                        if (valueForeignKeyRef is null)
                        {
                            throw new SemanticAnalyzerException(string.Format("Unable to resolve type {0} in current context", flatNode.Value.ForeignKeyName));
                        }
                        if (!IsSubclassOf(valueForeignKeyRef, flatNode.ForeignKeyRef))
                        {
                            throw new SemanticAnalyzerException(string.Format("Value FK reference class {0} is not a subclass of expected type {1}", flatNode.Value.ForeignKeyName, flatNode.ForeignKeyName));
                        }
                        ResolveReferencesRecord(valueForeignKeyRef, resolutionStack);
                        flatNode.Value.ForeignKeyRef = valueForeignKeyRef;
                    }
                }

                if (flatNode.Value.IsArray == flatNode.IsArray && flatNode.Value.ValueType == flatNode.FlatType)
                {
                    // Do nothing
                }
                else
                {
                    if (flatNode.Value.ValueType == FlatTypes.String && flatNode.FlatType == FlatTypes.ForeignKey)
                    {
                        if (!flatNode.Value.StringValue().Equals(string.Empty))
                        {
                            var stringValue = flatNode.Value.StringValue();
                            var valueForeignKeyRef = GlobalContext.ResolveRecordType(stringValue, packageContext);
                            if (valueForeignKeyRef is null)
                            {
                                throw new SemanticAnalyzerException(string.Format("Unable to resolve type {0} in current context", flatNode.Value.ForeignKeyName));
                            }
                            if (!IsSubclassOf(valueForeignKeyRef, flatNode.ForeignKeyRef))
                            {
                                throw new SemanticAnalyzerException(string.Format("Value FK reference class {0} is not a subclass of expected type {1}", stringValue, flatNode.ForeignKeyName));
                                // Console.WriteLine(string.Format("[ warning ] {0} is not a subclass of expected type {1} - as per game behaviour is left attached anyway", stringValue, flatNode.ForeignKeyName));
                            }
                            ResolveReferencesRecord(valueForeignKeyRef, resolutionStack);
                            flatNode.Value.ForeignKeyName = flatNode.Value.StringValue();
                            flatNode.Value.ForeignKeyRef = valueForeignKeyRef;
                        }
                    }
                    else if (flatNode.Value.ValueType == FlatTypes.Integer && flatNode.FlatType == FlatTypes.Float)
                    {
                        flatNode.Value.ValueType = FlatTypes.Float;
                    }
                    else if (flatNode.Value.ValueType == FlatTypes.String && (flatNode.FlatType == FlatTypes.CName || flatNode.FlatType == FlatTypes.LocKey || flatNode.FlatType == FlatTypes.ResRef))
                    {
                        // do nothing, this is fine
                    }
                    else if (flatNode.Value.ValueType == FlatTypes.Float && flatNode.FlatType == FlatTypes.Integer)
                    {
                        // Integer takes precedence in actual game -> unsure whether values are simply set to zero or rounded or truncated -> TODO: Test this
                        flatNode.Value.ValueType = FlatTypes.Integer;
                    }
                    else if (flatNode.Value.ValueType == FlatTypes.Vector3 && flatNode.FlatType == FlatTypes.EulerAngles)
                    {
                    }
                    else if (flatNode.Value.ValueType == FlatTypes.Vector4 && flatNode.FlatType == FlatTypes.Quaternion)
                    {
                    }
                    else
                    {
                        throw new SemanticAnalyzerException(string.Format("Cannot cast value type {0} to expected type {1}", flatNode.Value.ValueType, flatNode.FlatType));
                    }
                }

                if (flatNode.Value.ValueType == FlatTypes.ForeignKey)
                {
                    if (flatNode.Value.ForeignKeyRef is null)
                    {
                        throw new SemanticAnalyzerException(string.Format("Unresolved value foreign key type {0} for flat {1}", flatNode.Value.ForeignKeyName, flatNode.Name));
                    }

                    foreach (var node in flatNode.Value.GetChildren())
                    {
                        var foreignKeyInlineChild = node as FlatSemanticNode;
                        if (foreignKeyInlineChild is null)
                        {
                            continue;
                        }
                        var rootParentFlat = GlobalContext.FindRootInheritedFlat(flatNode.Value.ForeignKeyRef, foreignKeyInlineChild.Name, packageContext);
                        if (rootParentFlat is not null && rootParentFlat.ForeignKeyName != null && rootParentFlat.ForeignKeyRef is null)
                        {
                            var parentForeignKeyRef = GlobalContext.ResolveRecordType(rootParentFlat.ForeignKeyName, packageContext);
                            if (parentForeignKeyRef is null)
                            {
                                throw new SemanticAnalyzerException(string.Format("Flat {0} foreign key does not exist in global context {1}", rootParentFlat.Name, rootParentFlat.ForeignKeyName));
                            }
                            ResolveReferencesRecord(parentForeignKeyRef, resolutionStack);
                            rootParentFlat.ForeignKeyRef = parentForeignKeyRef;
                        }
                        ResolveReferencesFlat(foreignKeyInlineChild, resolutionStack, rootParentFlat);
                    }
                }
            }
            else
            {
                // is array
                if (!flatNode.Value.IsArray)
                {
                    throw new SemanticAnalyzerException(string.Format("Flat {0} is an array, but value is not - incompatible types", flatNode.Name));
                }
                // enforce type
                flatNode.Value.ValueType = flatNode.FlatType;
                flatNode.Value.ForeignKeyName = flatNode.ForeignKeyName;
                flatNode.Value.ForeignKeyRef = flatNode.ForeignKeyRef;

                // Checking that all are compatible as same types is done at semantic analysis stage
                foreach (var node in flatNode.Value.Children)
                {
                    var arrayItemChildValue = node as ValueSemanticNode;
                    if (arrayItemChildValue is null)
                    {
                        continue;
                    }

                    if (arrayItemChildValue.ValueType == FlatTypes.ForeignKey || arrayItemChildValue.ValueType == FlatTypes.InlineRecord)
                    {
                        if (flatNode.FlatType != FlatTypes.ForeignKey)
                        {
                            throw new SemanticAnalyzerException(string.Format("Foreign key array item encountered in non-foreign key flat {0}", flatNode.Name));
                        }
                        if (arrayItemChildValue.ForeignKeyName is null)
                        {
                            // Inline records with no inherited type get the type of the parent implicitly
                            arrayItemChildValue.ForeignKeyName = flatNode.Value.ForeignKeyName;
                            arrayItemChildValue.ForeignKeyRef = flatNode.Value.ForeignKeyRef;
                        }
                        else
                        {
                            var recordReference = GlobalContext.ResolveRecordType(arrayItemChildValue.ForeignKeyName, packageContext);
                            if (recordReference is null)
                            {
                                throw new SemanticAnalyzerException(string.Format("Unable to resolve string foreign key reference {0} in current context", arrayItemChildValue.StringValue()));
                            }
                            ResolveReferencesRecord(recordReference, resolutionStack);
                            // TODO: Keep this warning here for reference - is the null analyzer dumb as bricks or am I?
                            if (!IsSubclassOf(recordReference, flatNode.ForeignKeyRef))
                            {
                                // Game accepts this! See: DashAndDodgeActions.DashSandevistanSprintLeftDefinition.loopSubActions
                                throw new SemanticAnalyzerException(string.Format("{1} is not a subclass of expected type {2}", recordReference.Name, flatNode.ForeignKeyName));
                                // Console.WriteLine(string.Format("[ warning ] {0} is not a subclass of expected type {1} - as per game behaviour is left attached anyway", recordReference.Name, flatNode.ForeignKeyName));
                            }
                            arrayItemChildValue.ForeignKeyRef = recordReference;
                        }
                        arrayItemChildValue.ValueType = FlatTypes.ForeignKey;

                        // TODO: Because I'm a moron, I now have to individually iterate flat children -> NEEDS TO BE CHANGED TO INLINE RECORD IN ANALYZER
                        foreach (var arrayItemNode in arrayItemChildValue.GetChildren())
                        {
                            var foreignKeyInlineChild = arrayItemNode as FlatSemanticNode;
                            if (foreignKeyInlineChild is null)
                            {
                                continue;
                            }

                            var rootParentFlat = GlobalContext.FindRootInheritedFlat(arrayItemChildValue.ForeignKeyRef, foreignKeyInlineChild.Name, packageContext);
                            if (rootParentFlat is not null && rootParentFlat.ForeignKeyName != null && rootParentFlat.ForeignKeyRef is null)
                            {
                                var parentForeignKeyRef = GlobalContext.ResolveRecordType(rootParentFlat.ForeignKeyName, packageContext);
                                if (parentForeignKeyRef is null)
                                {
                                    throw new SemanticAnalyzerException(string.Format("Flat {0} foreign key does not exist in global context {1}", rootParentFlat.Name, rootParentFlat.ForeignKeyName));
                                }
                                ResolveReferencesRecord(parentForeignKeyRef, resolutionStack);
                                rootParentFlat.ForeignKeyRef = parentForeignKeyRef;
                            }

                            ResolveReferencesFlat(foreignKeyInlineChild, resolutionStack, rootParentFlat);
                        }
                    }
                    else if (arrayItemChildValue.ValueType == FlatTypes.String && flatNode.FlatType == FlatTypes.ForeignKey)
                    {
                        if (!arrayItemChildValue.StringValue().Equals(string.Empty))
                        {
                            var valueString = arrayItemChildValue.StringValue();
                            var recordReference = GlobalContext.ResolveRecordType(valueString, packageContext);
                            if (recordReference is null)
                            {
                                throw new SemanticAnalyzerException(string.Format("Unable to resolve string foreign key reference {0} in current context", arrayItemChildValue.StringValue()));
                            }
                            ResolveReferencesRecord(recordReference, resolutionStack);
                            arrayItemChildValue.ForeignKeyName = arrayItemChildValue.StringValue();
                            arrayItemChildValue.ForeignKeyRef = recordReference;
                        }
                    }
                    else if (arrayItemChildValue.ValueType == flatNode.FlatType)
                    {
                    }
                    else if (arrayItemChildValue.ValueType == FlatTypes.Integer && flatNode.FlatType == FlatTypes.Float)
                    {
                        arrayItemChildValue.ValueType = FlatTypes.Float;
                    }
                    else if (arrayItemChildValue.ValueType == FlatTypes.Float && flatNode.FlatType == FlatTypes.Integer)
                    {
                        // Integer takes precedence in actual game -> unsure whether values are simply set to zero or rounded or truncated -> TODO: Test this
                        // TODO: Cite example - AIActions somewhere
                        arrayItemChildValue.ValueType = FlatTypes.Integer;
                    }
                    else if (arrayItemChildValue.ValueType == FlatTypes.String && (flatNode.FlatType == FlatTypes.CName || flatNode.FlatType == FlatTypes.ResRef || flatNode.FlatType == FlatTypes.LocKey))
                    {
                    }
                    else if (flatNode.Value.ValueType == FlatTypes.Vector3 && flatNode.FlatType == FlatTypes.EulerAngles)
                    {
                    }
                    else if (flatNode.Value.ValueType == FlatTypes.Vector4 && flatNode.FlatType == FlatTypes.Quaternion)
                    {
                    }
                    else
                    {
                        throw new SemanticAnalyzerException(string.Format("Cannot cast value of type {0} to expected type {1} in flat {2}", arrayItemChildValue.ValueType, flatNode.FlatType, flatNode.Name));
                    }
                }
            }
            resolutionStack.Pop();
            return flatNode;
        }

        protected RecordSemanticNode ResolveReferencesRecord(RecordSemanticNode recordNode, Stack<SemanticNode> resolutionStack)
        {
            if (recordNode.IsFullyResolved || resolutionStack.Contains(recordNode))
            {
                return recordNode;
            }
            resolutionStack.Push(recordNode);
            // Console.WriteLine(recordNode.Name);
            var packageContext = recordNode.PackageContext;
            analyzerLog!.WriteLine(string.Format("Resolving record {0}", recordNode.Name));

            RecordSemanticNode? parentRecord = null;

            var parentType = recordNode.InheritedFrom;
            if (parentType is null)
            {
                if (packageContext.Name is not null && packageContext.Name.Equals("RTDB"))
                {
                    analyzerLog!.WriteLine(string.Format("RTDB package {0} is base level schema", recordNode.Name));
                }
                else if (recordNode.Parent!.Type == SemanticNodeTypes.Root)
                {
                    recordNode.IsGroup = true;
                    analyzerLog.WriteLine(string.Format("  Record {0} changed to group", recordNode.Name));
                }
                else
                {
                    throw new SemanticAnalyzerException("Inline record has anonymous type");
                }
            }
            else
            {
                parentRecord = GlobalContext.ResolveRecordType(parentType, packageContext);
                if (parentRecord is null)
                {
                    throw new SemanticAnalyzerException(string.Format("Record {0} attempts to inherit from non-existent type {1}", recordNode.Name, parentType));
                }
                if (parentRecord.IsGroup)
                {
                    recordNode.IsGroup = true;
                    analyzerLog.WriteLine(string.Format("   Validated record {0} with type {1} --- GROUP TYPE ---", recordNode.Name, parentType));
                }
                else
                {
                    analyzerLog!.WriteLine(string.Format("   Validated record {0} with type {1}", recordNode.Name, parentType));
                }
                ResolveReferencesRecord(parentRecord, resolutionStack);
                recordNode.InheritedFromRef = parentRecord;
            }

            foreach (var flatNode in recordNode.GetChildren().Where(y => y is FlatSemanticNode).Select(z => z as FlatSemanticNode))
            {
                if (flatNode is null)
                {
                    continue;
                }
                var parentRecordFlat = parentRecord is null ? null : GlobalContext.FindRootInheritedFlat(parentRecord, flatNode.Name, packageContext);
                if (parentRecordFlat is not null && parentRecordFlat.ForeignKeyName != null && parentRecordFlat.ForeignKeyRef is null)
                {
                    var parentForeignKeyRef = GlobalContext.ResolveRecordType(parentRecordFlat.ForeignKeyName, packageContext);
                    if (parentForeignKeyRef is null)
                    {
                        throw new SemanticAnalyzerException(string.Format("Flat {0} of record {1} foreign key does not exist in global context {2}", parentRecordFlat.Name, recordNode.Name, parentRecordFlat.ForeignKeyName));
                    }
                    ResolveReferencesRecord(parentForeignKeyRef, resolutionStack);
                    parentRecordFlat.ForeignKeyRef = parentForeignKeyRef;
                }
                ResolveReferencesFlat(flatNode, resolutionStack, parentRecordFlat);
            }
            recordNode.IsFullyResolved = true;
            resolutionStack.Pop();
            return recordNode;
        }

        protected bool IsSubclassOf(RecordSemanticNode subRecord, RecordSemanticNode superRecord)
        {
            if (subRecord == superRecord)
            {
                return true;
            }
            else
            {
                if (subRecord.InheritedFrom is null)
                {
                    // Time for special cases
                    // AIActionChangeNPCState -> AISubAction
                    if (subRecord.Name is null || superRecord.Name is null)
                    {
                        return false;
                    }
                    else if (subRecord.Name.Equals("AIActionChangeNPCState") && superRecord.Name.Equals("AISubAction"))
                    {
                        return true;
                    }
                    else if (subRecord.Name.Equals("Effector") && superRecord.Name.Equals("GameplayLogicPackage"))
                    {
                        return true;
                    }
                    else if (subRecord.Name.Equals("AISubAction") && superRecord.Name.Equals("AIActionLookAtData"))
                    {
                        return true;
                    }
                    else if (subRecord.Name.Equals("AISubAction") && superRecord.Name.Equals("ObjectActionEffect"))
                    {
                        return true;
                    }
                    else if (subRecord.Name.Equals("AIActionSubCondition") && superRecord.Name.Equals("AIActionCondition"))
                    {
                        return true;
                    }
                    else if (subRecord.Name.Equals("SearchFilterMaskTypeCondition") && superRecord.Name.Equals("SearchFilterMaskType"))
                    {
                        return true;
                    }
                    else if (subRecord.Name.Equals("StatModifier") && superRecord.Name.Equals("StatusEffectAttackData"))
                    {
                        return true;
                    }
                    else if (subRecord.Name.Equals("StatModifier") && superRecord.Name.Equals("StatModifierGroup"))
                    {
                        return true;
                    }
                    else if (subRecord.Name.Equals("IPrereq") && superRecord.Name.Equals("ObjectActionPrereq"))
                    {
                        return true;
                    }
                    return false;
                }
                if (subRecord.InheritedFromRef is null)
                {
                    var nextSuperRecord = GlobalContext.ResolveRecordType(subRecord.InheritedFrom, subRecord.PackageContext);
                    if (nextSuperRecord is null)
                    {
                        throw new SemanticAnalyzerException(string.Format("Unable to resolve parent {0} of record {1} in this context", subRecord.InheritedFrom, subRecord.Name));
                    }
                    return IsSubclassOf(nextSuperRecord, superRecord);
                }
                else
                {
                    return IsSubclassOf(subRecord.InheritedFromRef, superRecord);
                }
            }
        }

        //protected bool IsSubclassOf(RecordSemanticNode childRecordNode, string parentRecordName, PackageContextSemanticNode packageContext)
        //{
        //    // class name and record node must be full qualified
        //    var fullChildName = GlobalContext.FullyQualifiedName(childRecordNode);
        //    var fullParentName = GlobalContext.FullyQualifiedName(parentRecordName, packageContext);
        //    if (childRecordNode.Name is null)
        //    {
        //        throw new SemanticAnalyzerException("Null arguments provided for subclass resolution");
        //    }
        //    else if (fullChildName.Equals(fullParentName))
        //    {
        //        return true;
        //    }
        //    if (childRecordNode.InheritedFrom is null)
        //    {
        //        return false;
        //    }
        //    else
        //    {
        //        if (childRecordNode.InheritedFromRef is null)
        //        {
        //            var parentRecord = GlobalContext.ResolveRecordType(childRecordNode.InheritedFrom, childRecordNode.PackageContext);
        //            if (parentRecord is null)
        //            {
        //                throw new SemanticAnalyzerException(string.Format("Child node {0} refers to unresolvable parent {1} in current context"));
        //            }
        //            return IsSubclassOf(parentRecord, fullParentName, packageContext);
        //        }
        //        return IsSubclassOf(childRecordNode.InheritedFromRef, fullParentName, packageContext);
        //    }
        //}

        public void SanityCheck()
        {
            foreach (var rootNodeTuple in GlobalContext.RootNodes)
            {
                var rootNode = rootNodeTuple.Value;
                var packageContext = rootNode.GetRootPackageContext();

                foreach (var freeRecord in GlobalContext.FreeRecords)
                {
                    SanityCheckRecord(freeRecord);
                }
                foreach (var freeFlat in GlobalContext.FreeFlats)
                {
                    SanityCheckFlat(freeFlat);
                }
                foreach (var package in GlobalContext.Packages)
                {
                    foreach (var packageRecord in package.Records)
                    {
                        SanityCheckRecord(packageRecord);
                    }
                    foreach (var packageFlat in package.Flats)
                    {
                        SanityCheckFlat(packageFlat);
                    }
                }
            }
        }

        public void SanityCheckRecord(RecordSemanticNode record)
        {
            if (!record.IsFullyResolved)
            {
                throw new SemanticAnalyzerException(string.Format("Record {0} is not fully resolved", record.Name));
            }
            if (record.InheritedFrom != null && record.InheritedFromRef is null)
            {
                throw new SemanticAnalyzerException(string.Format("Record {0} inherits from {1} but reference has not been resolved", record.Name, record.InheritedFrom));
            }
            foreach (var flatNode in record.GetChildren().Where(x => x is FlatSemanticNode).Select(y => y as FlatSemanticNode))
            {
                SanityCheckFlat(flatNode!);
            }
        }

        public void SanityCheckFlat(FlatSemanticNode flatNode)
        {
            if (flatNode.FlatType == FlatTypes.Unresolved || flatNode.FlatType == FlatTypes.InlineRecord)
            {
                throw new SemanticAnalyzerException(string.Format("Flat {0} has unresolved type: {1}", flatNode.Name, flatNode.FlatType));
            }
            if (flatNode.FlatType == FlatTypes.ForeignKey)
            {
                if (flatNode.ForeignKeyName == null)
                {
                    throw new SemanticAnalyzerException(string.Format("Flat {0} has ForeignKey type but no name for reference", flatNode.Name));
                }
                if (flatNode.ForeignKeyRef == null)
                {
                    throw new SemanticAnalyzerException(string.Format("Flat {0} has ForeignKey type and Foreign Key name {1} but no resolved reference", flatNode.Name, flatNode.ForeignKeyName));
                }
            }
            SanityCheckValue(flatNode.Value);
        }

        public void SanityCheckValue(ValueSemanticNode valueNode)
        {
            if (valueNode.ValueType == FlatTypes.String && valueNode.ForeignKeyName is not null)
            {
                if (valueNode.ForeignKeyRef is null)
                {
                    throw new SemanticAnalyzerException(string.Format("Value (string foreign key) has unresolved foreign key name {0}", valueNode.ForeignKeyName));
                }
            }
            if (valueNode.ValueType == FlatTypes.String && valueNode.ForeignKeyName is null &&  valueNode.ForeignKeyRef is not null)
            {
                throw new SemanticAnalyzerException("Inconsistent labelling of value nodes for foreign key flats - non-null FK reference but null FK name");
            }
            if (valueNode.ValueType == FlatTypes.ForeignKey)
            {
                if (valueNode.ForeignKeyName is null)
                {
                    throw new SemanticAnalyzerException("Value is marked as foreign key but has no foreign key name");
                }
                if (valueNode.ForeignKeyRef is null)
                {
                    throw new SemanticAnalyzerException(string.Format("Value has unresolved foreign key name {0}", valueNode.ForeignKeyName));
                }
            }

            var parentFlat = valueNode.Parent as FlatSemanticNode;
            if (parentFlat is not null)
            {
                if (parentFlat.FlatType == FlatTypes.ForeignKey && valueNode.ValueType == FlatTypes.String && valueNode.StringValue().Length != 0 && valueNode.ForeignKeyName is null)
                {
                    throw new SemanticAnalyzerException(string.Format("String value '{0}' of foreign key flat {0} (key: {1}) has not been resolved", valueNode.StringValue(), parentFlat.Name, parentFlat.ForeignKeyName));
                }
            }

            foreach (var flatChild in valueNode.GetChildren().Where(x => x is FlatSemanticNode).Select(x => x as FlatSemanticNode))
            {
                SanityCheckFlat(flatChild!);
            }
            foreach (var valueChild in valueNode.GetChildren().Where(y => y is ValueSemanticNode).Select(y => y as ValueSemanticNode))
            {
                SanityCheckValue(valueChild!);
            }
        }

        public RecordSemanticNode FlattenRecordByName(string name)
        {
            analyzerLog = new StreamWriter("flattened.log");
            var record = GlobalContext.ResolveRecordType(name, null);
            if (record is null)
            {
                throw new SemanticAnalyzerException(string.Format("Unable to resolve record '{0}'", name));
            }
            var returnRecord = FlattenRecord(record, new Stack<RecordSemanticNode>(), new FlattenRules(new List<string> { "Vehicle" }));
            analyzerLog.Flush();
            analyzerLog.Close();
            analyzerLog = new StreamWriter("flattened-result.log");
            record.TweakPrint(string.Empty, analyzerLog);
            analyzerLog.WriteLine("--- FLATTENED START ---");
            returnRecord.TweakPrint(string.Empty, analyzerLog);
            analyzerLog.Flush();
            analyzerLog.Close();
            return returnRecord;
        }

        public RecordSemanticNode FlattenRecord(RecordSemanticNode recordNode, Stack<RecordSemanticNode> resolutionStack, FlattenRules flattenRules)
        {
            if (resolutionStack.Contains(recordNode))
            {
                /*
                if (recordNode.Name.Contains("crowd_int_apparel_002__umbrella_small"))
                {
                    analyzerLog.WriteLine(string.Format("[ warning ] Circular reference found while flattening: {0} referenced from {1} but already in stack", recordNode.Name, resolutionStack.First().Name));
                    return recordNode;
                }
                */
                analyzerLog.Flush();
                throw new SemanticAnalyzerException(string.Format("Circular reference in resolution stack: {0}", recordNode.Name));
            }

            var recordCopy = new RecordSemanticNode(recordNode.Name, recordNode.InheritedFrom);
            recordCopy.InheritedFromRef = recordNode.InheritedFromRef;
            recordCopy.IsGroup = recordNode.IsGroup;
            recordCopy.IsFullyResolved = recordNode.IsFullyResolved;

            var readableRecordName = recordNode.Name;
            if (readableRecordName == null) { readableRecordName = "<inline>"; }

            analyzerLog.WriteLine(string.Format("[ Record '{0}' ]", readableRecordName));

            /// WARNING: MOVED - from after parent resolution?
            resolutionStack.Push(recordNode);

            if (recordNode.Name == "ChemicalDamage")
            {
                Console.WriteLine("DEBUG: Break here");
            }

            if (recordNode.InheritedFromRef is not null)
            {
                analyzerLog.WriteLine(string.Format("[ Record '{0}' ] Attempting to flatten parent type {1}", readableRecordName, recordNode.InheritedFromRef.Name));
                var flattenedParent = FlattenRecord(recordNode.InheritedFromRef, resolutionStack, flattenRules);
                analyzerLog.WriteLine(string.Format("[ Record '{0}' ] Successfully flattened parent type '{1}'", readableRecordName, flattenedParent.Name));
                foreach (var parentFlat in flattenedParent.GetFlats())
                {
                    var overridingFlat = recordNode.HasFlatWithName(parentFlat.Name);
                    if (overridingFlat is null)
                    {
                        analyzerLog.WriteLine(string.Format("[ Record '{0}' ] Adding legacy parent flat {1}", readableRecordName, parentFlat.Name));
                        recordCopy.AddChild(parentFlat);
                    }
                    else
                    {
                        analyzerLog.WriteLine(string.Format("[ Record '{0}' ] flattening and merging parent flat '{1}'", readableRecordName, parentFlat.Name));
                        recordCopy.AddChild(FlattenFlat(overridingFlat, recordNode, (resolutionStack), flattenRules).MergeFrom(parentFlat));
                    }
                }
                foreach (var flat in recordNode.GetFlats())
                {
                    if (recordCopy.HasFlatWithName(flat.Name) is null)
                    {
                        analyzerLog.WriteLine(string.Format("[ Record '{0}' ] flattening and add novel flat '{1}'", readableRecordName, flat.Name));
                        recordCopy.AddChild(FlattenFlat(flat, recordNode, resolutionStack, flattenRules));
                    }
                }
            }
            else
            {
                // Base class -> flatten all our flats
                analyzerLog.WriteLine(string.Format("[ Record '{0}' ] Base class -> no inheritance resolved", readableRecordName));
                foreach (var flat in recordNode.GetFlats())
                {
                    analyzerLog.WriteLine(string.Format("[ Record '{0}' ] flattening '{1}'...", readableRecordName, flat.Name));
                    recordCopy.AddChild(FlattenFlat(flat, recordNode, resolutionStack, flattenRules));
                }
            }

            /*  What was this for?
            foreach (var thisFlats in recordNode.GetFlats())
            {
                if (recordCopy.HasFlatWithName(thisFlats.Name) is null)
                {
                    recordCopy.AddChild(thisFlats);
                }
            }
            */
            analyzerLog.WriteLine(string.Format("[ Record '{0}' ] Complete.", readableRecordName));
            resolutionStack.Pop();
            return recordCopy;
        }

        public FlatSemanticNode FlattenFlat(FlatSemanticNode flatNode, RecordSemanticNode parentRecord, Stack<RecordSemanticNode> resolutionStack, FlattenRules flattenRules)
        {
            var flatCopy = new FlatSemanticNode(flatNode.Name, flatNode.FlatType, flatNode.IsArray, FlattenValue(flatNode.Value, flatNode, parentRecord, resolutionStack, flattenRules), flatNode.OperatorType);
            flatCopy.ForeignKeyName = flatNode.ForeignKeyName;
            flatCopy.ForeignKeyRef = flatNode.ForeignKeyRef;
            flatCopy.PackageContext = flatNode.PackageContext;
            return flatCopy;
        }

        public ValueSemanticNode FlattenValue(ValueSemanticNode valueNode, FlatSemanticNode parentFlat, RecordSemanticNode parentRecord, Stack<RecordSemanticNode> resolutionStack, FlattenRules flattenRules)
        {
            // Structure:
            // Array:
            //    Scalar -> children are scalar ValueSemanticNodes
            //    Vector -> children are vector ValueSemanticNodes
            //    InlineRecord/ForeignKey -> children are ValueSemanticNodes of type ForeignKey/InlineRecord
            // Non-array:
            //    Scalar -> syntax nodes only
            //    Vector -> syntax nodes only
            //    InlineRecord/ForeignKey -> children are FlatSemanticNodes (this really should be a child RecordSemanticNode - TODO: CHRIS THIS IS YOUR MISTAKE)

            // If array -> process children (w/ parent flat type ?)
            // else:
            //     string & foreignKeyName is not null -> flatten record @ reference
            //     Scalar -> copy
            //     Vector -> copy
            //     ForeignKey -> copy, new RecordSemanticNode with relevant information including duplicated flats -> multiple FlattenFlat(), then FlattenRecord()
            //     Unresolved -> exception!
            if (valueNode.IsArray)
            {
                var valueCopy = new ValueSemanticNode(valueNode.ValueType, valueNode.IsArray, valueNode.SyntaxNodes);
                foreach (var child in valueNode.Children)
                {
                    var valueChild = child as ValueSemanticNode;
                    if (valueChild is not null)
                    {
                        valueCopy.Children.Add(FlattenValue(valueChild, parentFlat, parentRecord, resolutionStack, flattenRules));
                        continue;
                    }
                    else
                    {
                        throw new SemanticAnalyzerException(string.Format("Found non-Value child for value of flat {0} which is array of type {1}", parentFlat.Name, valueNode.ValueType));
                    }
                }
                return valueCopy;
            }
            else if (valueNode.ValueType == FlatTypes.String && valueNode.ForeignKeyRef is not null)
            {
                if (valueNode.ForeignKeyRef is null)
                {
                    throw new SemanticAnalyzerException(string.Format("Value of flat {0} is foreign key but has no resolved reference", parentFlat.Name));
                }
                var valueCopy = new ValueSemanticNode(valueNode.ValueType, valueNode.IsArray, valueNode.SyntaxNodes);
                valueCopy.ForeignKeyName = valueNode.ForeignKeyName;
                valueCopy.ForeignKeyRef = valueNode.ForeignKeyRef;
                if (flattenRules.StringKeyAllowedPackages.Contains(valueNode.ForeignKeyRef.PackageContext.Name))
                {
                    try
                    {
                        var flattenedReference = FlattenRecord(valueNode.ForeignKeyRef, resolutionStack, flattenRules);
                        valueCopy.Children.Add(flattenedReference);
                    }
                    catch (SemanticAnalyzerException sae)
                    {
                        // Likely circular reference
                        // TODO: A better way
                        if (!sae.Message.Contains("Circular") && !sae.Message.Contains("circular"))
                        {
                            throw new SemanticAnalyzerException("Propagated exception: " + sae.Message);
                        }
                        analyzerLog.WriteLine(string.Format("[ warning ] Circular reference found attempting to resolve '{0}' - keeping as string with {1} children", valueNode.ForeignKeyName, valueCopy.Children.Count));
                        // no children + foreignKeyRef is not null -> marks circular reference, keep as string
                    }
                }
                else
                {
                    analyzerLog.WriteLine(string.Format("[ info ] Skipping reference {0} as its package {1} is not in allowed string key packages", valueCopy.ForeignKeyRef.Name, valueCopy.ForeignKeyRef.PackageContext.Name));
                }
                
                return valueCopy;
            }
            else if (valueNode.IsScalar() || valueNode.IsVector())
            {
                return new ValueSemanticNode(valueNode.ValueType, valueNode.IsArray, valueNode.SyntaxNodes);
            }
            else if (valueNode.ValueType == FlatTypes.ForeignKey)
            {
                // throw new SemanticAnalyzerException(string.Format("Ambiguous structure found for flat {0} -> rethink analysis phase", parentFlat.Name));
                var newRecordNode = new RecordSemanticNode(null, valueNode.ForeignKeyName);
                if (valueNode.ForeignKeyRef is null)
                {
                    throw new SemanticAnalyzerException(string.Format("Foreign key reference is null on foreign key value type from flat {0}", parentFlat.Name));
                }
                newRecordNode.InheritedFromRef = valueNode.ForeignKeyRef;
                newRecordNode.PackageContext = parentRecord.PackageContext;
                newRecordNode.IsFullyResolved = parentRecord.IsFullyResolved;
                newRecordNode.IsGroup = newRecordNode.InheritedFromRef.IsGroup;
                if (!valueNode.GetChildren().All(node => node is FlatSemanticNode))
                {
                    throw new SemanticAnalyzerException(string.Format("Invalid child nodes on value from flat {0} - all need to be FlatSemanticNode instances"));
                }
                newRecordNode.GetChildren().AddRange(valueNode.GetChildren());
                var returnNode = FlattenRecord(newRecordNode, resolutionStack, flattenRules);
                var valueCopy = new ValueSemanticNode(valueNode.ValueType, valueNode.IsArray, valueNode.SyntaxNodes);
                valueCopy.ForeignKeyName = valueNode.ForeignKeyName;
                valueCopy.ForeignKeyRef = valueNode.ForeignKeyRef;
                valueCopy.GetChildren().Add(returnNode);
                return valueCopy;
            }
            else
            {
                throw new SemanticAnalyzerException(string.Format("Unresolved value type for flat {0}", parentFlat.Name));
            }
        }

    }
}
