using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TweakParser
{
    public class SyntaxNode
    {
        protected List<SyntaxNode> _children;
        protected SyntaxNode? _parent;
        protected List<Token>? _tokenData;
        public string Type { get; set; }
        public string? Value { get; set; }

        public SyntaxNode(SyntaxNode? parent, List<SyntaxNode> children, List<Token>? tokenData, string type, string? value)
        {
            _parent = parent;
            _children = children;
            _tokenData = tokenData;
            Type = type;
            Value = value;
        }

        public SyntaxNode(List<Token> tokenData, string type, string? value) : this(null, new List<SyntaxNode>(), tokenData, type, value) { }

        public SyntaxNode(Token? token, string type, string? value) : this(null, new List<SyntaxNode>(), null, type, value)
        {
            _tokenData = new List<Token>();
            if (token is not null)
            { 
                _tokenData.Add(token);
            }
        }

        public SyntaxNode(string type) : this(null, new List<SyntaxNode>(), null, type, null) { }

        public SyntaxNode(string type, string value) : this(null, new List<SyntaxNode>(), null, type, value) { }

        public void AddChild(SyntaxNode child)
        {
            _children.Add(child);
            child._parent = this;
        }

        public void RemoveChild(SyntaxNode child)
        {
            _children.Remove(child);
            child._parent = null;
        }

        public void AddChildren(List<SyntaxNode> children)
        {
            _children.AddRange(children);
            foreach (var child in children)
            {
                child._parent = this;
            }
        }

        public List<SyntaxNode> GetChildren()
        {
            return _children;
        }

        public SyntaxNode? GetParent()
        {
            return _parent;
        }

        public List<Token>? GetTokenData()
        {
            return _tokenData;
        }

        public void Print(string spacing = "")
        {
            Console.WriteLine(string.Format("{0}{1} : {2}", spacing, Type, Value));
            foreach (var child in _children)
            {
                child.Print(spacing + "    ");
            }
        }

        public string GetValueOrEmpty()
        {
            if (Value is null)
            {
                return string.Empty;
            }
            return Value;
        }
    }
}
