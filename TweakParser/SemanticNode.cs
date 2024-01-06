using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.ConstrainedExecution;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace TweakParser
{
    public enum FlatTypes
    {
        Unresolved,
        String,
        Integer,
        Float,
        Vector2,
        Vector3,
        Vector4,
        EulerAngles,
        Quaternion,
        Boolean,
        CName,
        LocKey,
        ResRef,
        ForeignKey,
        InlineRecord,
        Identifier
    }

    public enum OperatorTypes
    {
        Assign,
        Append
    }

    public class SemanticNode
    {
        public enum SemanticNodeTypes
        {
            Root,
            PackageContext,
            UsingDeclaration,
            RecordDefinition,
            GroupDefinition,
            FlatDefinition,
            Value
        }

        public List<SemanticNode> Children { get; set; } = [];
        public SemanticNode? Parent {  get; set; }

        public SemanticNodeTypes Type { get; protected set;  }

        public SemanticNode()
        {
            Type = SemanticNodeTypes.Root;
        }

        public void AddChild(SemanticNode node)
        {
            Children.Add(node);
            node.Parent = this;
        }

        public void AddChildRange(IEnumerable<SemanticNode> nodes)
        {
            Children.AddRange(nodes);
            nodes.ToList().ForEach(x => x.Parent = this);
        }

        public List<SemanticNode> GetChildren()
        {
            return Children;
        }

        public virtual void Print(string spacing, StreamWriter? output = null)
        {
            if (output is null)
            {
                output = new StreamWriter(Console.OpenStandardOutput());
            }
            output.WriteLine(string.Format("{0}{1}", spacing, Type));
            foreach (var child in Children)
            {
                child.Print(spacing + "    ", output);
            }
        }

        public virtual void TweakPrint(string spacing, StreamWriter? output = null)
        {
            if (output is null)
            {
                output = new StreamWriter(Console.OpenStandardOutput());
            }
            foreach(var child in Children)
            {
                child.TweakPrint(spacing, output);
                output.WriteLine();
            }
        }

        public SemanticNode GetRootNode()
        {
            if (Type == SemanticNodeTypes.Root)
            {
                return this;
            }    
            else if (Parent is null)
            {
                throw new SemanticAnalyzerException("Invalid structure: Unable to find root node");
            }
            else
            {
                return Parent.GetRootNode();
            }
        }

        public PackageContextSemanticNode GetRootPackageContext()
        {
            if (Type != SemanticNodeTypes.Root)
            {
                var rootNode = GetRootNode();
                var packageContext = rootNode.GetRootPackageContext();
                return packageContext;
            }
            else
            {
                var packageContext = Children.FirstOrDefault(x => x.Type == SemanticNodeTypes.PackageContext) as PackageContextSemanticNode;
                if (packageContext is null)
                {
                    throw new SemanticAnalyzerException("Root node has no package context");
                }
                return packageContext;
            }
        }
    }

    public class PackageContextSemanticNode : SemanticNode
    {
        public string? Name { get; set; }
        public List<string> UsingPackageReferences { get; set; }

        public PackageContextSemanticNode(string? name)
        {
            Name = name;
            UsingPackageReferences = [];
            Type = SemanticNodeTypes.PackageContext;
        }
    }

    public class RecordSemanticNode : SemanticNode
    {
        public string? Name { get; set; } // null means inline
        public bool IsGroup { get; set; }
        public string? InheritedFrom { get; set; } // If null, means this is a base record i.e. part of the RTDB package
        public RecordSemanticNode? InheritedFromRef {  get; set; } // If InheritedFrom not null but this is, means UNRESOLVED
        public bool IsFullyResolved {  get; set; }

        public PackageContextSemanticNode PackageContext { get; set; }

        // TODO: Consider having BaseName and PackageName members ?

        public RecordSemanticNode(string? name, string? inheritedFrom) : base()
        {
            
            Name = name;
            InheritedFrom = inheritedFrom;
            InheritedFromRef = null;
            IsGroup = false;
            Type = SemanticNodeTypes.RecordDefinition;
            IsFullyResolved = false;
            PackageContext = new(null);
        }

        public List<FlatSemanticNode> GetFlats()
        {
            var result = new List<FlatSemanticNode>();

            foreach (var node in Children)
            {
                var flatNode = node as FlatSemanticNode;
                if (flatNode is not null)
                {
                    result.Add(flatNode);
                }
            }
            return result;
        }

        public string? BaseName()
        {
            if (Name is null)
            {
                return null;
            }
            if (Name.Contains('.'))
            {
                var identifiers = Name.Split('.');
                if (identifiers.Length > 2) 
                {
                    throw new SemanticAnalyzerException(string.Format("Invalid name encountered when attempting to extract base name: {0}", Name));
                }
                return identifiers[0];
            }
            else
            {
                return Name;
            }
        }

        public FlatSemanticNode? HasFlatWithName(string name)
        {
            foreach (var childNode in Children.Where(x => x is FlatSemanticNode))
            {
                var flatNode = childNode as FlatSemanticNode;
                if (flatNode is null)
                {
                    continue;
                }
                if (flatNode.Name.Equals(name))
                {
                    return flatNode;
                }
            }
            return null;
        }

        public override void Print(string spacing, StreamWriter? output = null)
        {
            if (output is null)
            {
                output = new StreamWriter(Console.OpenStandardOutput());
            }
            output.WriteLine(string.Format("{0}{1} - {2} : {3}", spacing, Type, Name, InheritedFrom));
            foreach (var child in Children)
            {
                child.Print(spacing + "    ", output);
            }
        }

        public override void TweakPrint(string spacing, StreamWriter? output = null)
        {
            if (output is null)
            {
                output = new StreamWriter(Console.OpenStandardOutput());
            }
            output.Write(spacing);
            if (Name is not null)
            {
                output.Write(Name + " : " + InheritedFrom + " ");
            }
            output.WriteLine("{");
            foreach (var child in Children)
            {
                child.TweakPrint(spacing + "    ", output);
            }
            output.WriteLine(spacing + "}" + (Name is null ? " : " + InheritedFrom : ""));
            //if (Name is null)
            //{
            //    Console.WriteLine(" : " + InheritedFrom);
            //}
            //else
            //{
            //    Console.WriteLine("d");
            //}
        }
    }

    public class ValueSemanticNode : SemanticNode
    {
        public FlatTypes ValueType { get; set; }
        public bool IsArray { get; set; }
        public List<SyntaxNode> SyntaxNodes { get; set; } = [];
        public string? ForeignKeyName { get; set; }
        public RecordSemanticNode? ForeignKeyRef { get; set; }

        public ValueSemanticNode(FlatTypes flatType, bool isArray, List<SyntaxNode> syntaxNodes)
        {
            ValueType = flatType;
            IsArray = isArray;
            SyntaxNodes = syntaxNodes;
            ForeignKeyName = null;
            ForeignKeyRef = null;
            Type = SemanticNodeTypes.Value;
        }

        public int ArrayLength()
        {
            if (!IsArray) { return 0; }
            else { return Children.Count; }
        }

        public bool IsScalar()
        {
            switch (ValueType)
            {
                case FlatTypes.String:
                case FlatTypes.Integer:
                case FlatTypes.Float:
                case FlatTypes.Boolean:
                case FlatTypes.CName:
                case FlatTypes.LocKey:
                case FlatTypes.ResRef:
                    return true;
                default: return false;
            }
        }

        public bool IsVector()
        {
            switch (ValueType)
            {
                case FlatTypes.Vector2:
                case FlatTypes.EulerAngles:
                case FlatTypes.Vector3:
                case FlatTypes.Quaternion:
                case FlatTypes.Vector4:
                    return true;
                default: return false;
            }
        }

        protected string ReadScalarData()
        {
            if (!IsScalar())
            {
                throw new SemanticAnalyzerException(string.Format("Unable to read scalar data for values of type {0}", ValueType));
            }
            var firstNode = SyntaxNodes.FirstOrDefault();
            if (firstNode is null || firstNode.Value is null)
            {
                return string.Empty;
            }
            return firstNode.Value;
        }

        protected List<string> ReadVectorData()
        {
            List<string> vectorValues = new();

            string[] subNodeTypes = [ "x", "y", "z", "a" ];

            var numValuesExpected = 2;
            if (!IsVector())
            {
                throw new SemanticAnalyzerException(string.Format("Unable to read vector data for values of type {0}", ValueType));
            }

            if (ValueType == FlatTypes.Vector3 || ValueType == FlatTypes.EulerAngles)
            {
                numValuesExpected = 3;
            }
            else if (ValueType == FlatTypes.Vector4 || ValueType == FlatTypes.Quaternion)
            {
                numValuesExpected = 4;
            }
            var vectorNode = SyntaxNodes.FirstOrDefault(node => node.Type.StartsWith("value-vector"));
            if (vectorNode is null)
            {
                throw new SemanticAnalyzerException(string.Format("No vector value syntax node found when attempting to read vector data - check value type"));
            }
            for (int i = 0; i < numValuesExpected; i++)
            {
                var nodeValue = vectorNode.GetChildren().FirstOrDefault(node => node.Type.Equals(subNodeTypes[i]));
                if (nodeValue is null || nodeValue.Value is null)
                {
                    throw new SemanticAnalyzerException(string.Format("Vector expecting at least {0} value(s) but syntax node with corresponding Type {1} either does not exist or has no value", numValuesExpected, subNodeTypes[i]));
                }
                vectorValues.Add(nodeValue.Value);
            }
            return vectorValues;
        }

        private static string VectorToString(List<string> vectorValues)
        {
            return string.Format("( {0} )", string.Join(", ", vectorValues));
        }

        public string StringValue()
        {
            if (IsArray) { return "{Array}"; }
            else if (IsScalar()) { return ReadScalarData(); }
            else if (IsVector()) { return VectorToString(ReadVectorData()); }
            else if (ValueType == FlatTypes.ForeignKey) { return string.Format("FK:{0}", ForeignKeyName); }
            else if (ValueType == FlatTypes.InlineRecord) { return "FK:{Unresolved)}"; }
            else { return "{Unresolved}"; }
        }

        public string TweakFormatValue()
        {
            var value = StringValue();
            if (ValueType == FlatTypes.String)
            {
                value = string.Format("\"{0}\"", value);
            }
            return value;
        }

        public override void Print(string spacing, StreamWriter? output = null)
        {
            if (output is null)
            {
                output = new StreamWriter(Console.OpenStandardOutput());
            }
            output.WriteLine(string.Format("{0}{1} - {2}{3} - {4}", spacing, Type, ValueType, IsArray ? "[]" : "", StringValue()));
            foreach (var child in Children)
            {
                child.Print(spacing + "    ", output);
            }
        }

        public override void TweakPrint(string spacing, StreamWriter? output = null)
        {
            if (output is null)
            {
                output = new StreamWriter(Console.OpenStandardOutput());
            }
            if (!IsArray || ValueType == FlatTypes.ForeignKey || (ValueType == FlatTypes.String && ForeignKeyName is not null))
            {
                foreach (var child in Children)
                {
                    child.TweakPrint(spacing + "    ", output);
                }
            }
            else
            {
                foreach (var child in Children)
                {
                    var valueChild = child as ValueSemanticNode;
                    if (valueChild is not null)
                    {
                        if (valueChild.ValueType != FlatTypes.ForeignKey)
                        {
                            output.WriteLine(spacing + "    " + valueChild.TweakFormatValue() + ",");
                        }
                        else
                        {
                            valueChild.Print(spacing + "    ", output);
                        }
                    }
                    var recordChild = child as RecordSemanticNode;
                    if (recordChild is not null)
                    {
                        recordChild.TweakPrint(spacing + "    ", output);
                        output.WriteLine(",");
                    }
                }
            }
        }
    }

    public class FlatSemanticNode : SemanticNode
    {
        public FlatTypes FlatType { get; set; }
        public bool IsArray { get; set; }
        public string Name { get; set; }
        public ValueSemanticNode Value { get; set; }
        public OperatorTypes OperatorType { get; set; }
        public string? ForeignKeyName { get; set; }
        public RecordSemanticNode? ForeignKeyRef { get; set; } // if null and FlatType is Unresolved, need to resolve
        public PackageContextSemanticNode PackageContext { get; set; }

        public FlatSemanticNode(string name, FlatTypes flatType, bool isArray, ValueSemanticNode value, OperatorTypes operatorType) : base()
        {
            Name = name;
            FlatType = flatType;
            IsArray = isArray;
            Value = value;
            OperatorType = operatorType;
            Type = SemanticNodeTypes.FlatDefinition;
            ForeignKeyName = null;
            AddChild(value);
            PackageContext = new(null);
        }

        public override void Print(string spacing, StreamWriter? output = null)
        {
            if (output is null)
            {
                output = new StreamWriter(Console.OpenStandardOutput());
            }
            output.WriteLine(string.Format("{0}{1} - {2} : {3}{4} : {5}", spacing, Type, Name, FlatType, IsArray ? "[]" : "", OperatorType));
            foreach (var child in Children)
            {
                child.Print(spacing + "    ", output);
            }
        }

        public override void TweakPrint(string spacing, StreamWriter? output = null)
        {
            if (output is null)
            {
                output = new StreamWriter(Console.OpenStandardOutput());
            }
            output.Write(spacing);
            if (FlatType == FlatTypes.ForeignKey)
            {
                output.Write("fk< " + ForeignKeyName + " >");
            }
            else
            {
                output.Write(FlatType);
            }
            if (IsArray)
            {
                output.Write("[]");
            }
            output.Write(" " + Name + " ");
            if (OperatorType == OperatorTypes.Assign)
            {
                output.Write("= ");
            }
            else
            {
                output.Write("+= ");
            }
            if (IsArray && Value.ArrayLength() > 0)
            {
                output.WriteLine("[");
                Value.TweakPrint(spacing, output);
            }
            else if (IsArray && Value.ArrayLength() == 0)
            {
                output.WriteLine("[];");
            }
            else if (FlatType != FlatTypes.ForeignKey)
            {
                output.WriteLine(Value.TweakFormatValue() + ";");
            }
            else if (FlatType == FlatTypes.ForeignKey && Value.ValueType == FlatTypes.String && Value.ForeignKeyRef is null)
            {
                output.WriteLine(Value.TweakFormatValue() + ";");
            }
            else if (FlatType == FlatTypes.ForeignKey && Value.ValueType == FlatTypes.String && Value.Children.Count == 0)
            {
                output.WriteLine(Value.TweakFormatValue() + ";");
            }
            else if (FlatType == FlatTypes.ForeignKey && Value.ValueType == FlatTypes.String && Value.StringValue().Length == 0)
            {
                output.WriteLine("\"\";");
            }    
            else
            {
                // output.WriteLine(string.Format("- {0} : {1} : {2} -", FlatType, Value.TweakFormatValue(), Value.ValueType));
                output.WriteLine();
                Value.TweakPrint(spacing, output);
            }

            if (IsArray && Value.ArrayLength() > 0)
            {
                output.WriteLine(spacing + "]");
            }
        }

        public void CheckTypeConsistency()
        {
            if (FlatType == FlatTypes.Unresolved)
            {
                return;
            }

            if (IsArray != Value.IsArray)
            {
                throw new SemanticAnalyzerException(string.Format("Flat '{0}' (type {1}) with value type {2} - both or neither must be arrays", Name, FlatType, Value.ValueType));
            }
            else if (FlatType == FlatTypes.Float)
            {
                if (Value.ValueType != FlatTypes.Float && Value.ValueType != FlatTypes.Integer)
                {
                    throw new SemanticAnalyzerException(string.Format("Flat '{0}' with type {1} cannot accept value of type {2} (must be integer or float)", Name, FlatType, Value.ValueType));
                }
            }
            else if (FlatType == FlatTypes.EulerAngles)
            {
                if (Value.ValueType != FlatTypes.Vector3)
                {
                    throw new SemanticAnalyzerException(string.Format("Flat '{0}' with type {1} cannot accept value of type {2} (needs vector3)", Name, FlatType, Value.ValueType));
                }

            }
            else if (FlatType == FlatTypes.Quaternion)
            {
                if (Value.ValueType != FlatTypes.Vector4)
                {
                    throw new SemanticAnalyzerException(string.Format("Flat '{0}' with type {1} cannot accept value of type {2} (needs vector4)", Name, FlatType, Value.ValueType));
                }
            }
            else if (FlatType == FlatTypes.CName || FlatType == FlatTypes.ResRef || FlatType == FlatTypes.LocKey)
            {
                if (Value.ValueType != FlatTypes.String)
                {
                    throw new SemanticAnalyzerException(string.Format("Flat '{0}' with type {1} cannot accept value of type {2} (needs string)", Name, FlatType, Value.ValueType));
                }
            }
            else if (FlatType != Value.ValueType)
            {
                throw new SemanticAnalyzerException(string.Format("Flat '{0}' with type {1} cannot accept value of type {2}", Name, FlatType, Value.ValueType));
            }
        }

        public FlatSemanticNode MergeFrom(FlatSemanticNode parentFlat)
        {
            if (parentFlat.Name != Name)
            {
                throw new SemanticAnalyzerException(string.Format("Flats {0} and {1} have different names and therefore cannot be merged"));
            }

            if (OperatorType == OperatorTypes.Assign)
            {
                return this;
            }

            if (!IsArray && OperatorType == OperatorTypes.Append)
            {
                throw new SemanticAnalyzerException(string.Format("Flat {0} cannot append to a non-array type", Name));
            }

            if (IsArray)
            {
                foreach (var child in parentFlat.Value.Children)
                {
                    Value.Children.Add(child);
                }
            }

            return this;
        }
    }
}
