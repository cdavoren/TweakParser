using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;

/*

BNF (old and wrong - look at EBNF):
 
<root> ::= <top-level> | <top-level> <root>

<top-level> ::= <package-declaration> | <using-declaration> | <record> | <EOF>

<package-declaration> ::= <keyword-package> <identifier>

<using-declaration> ::= <keyword-using> <package-list>

<package-list> ::= <identifier> | <identifier> <comma> <package-list>

<record> ::= <identifier> <flat-list> | <identifier> <record-inheritance> <flat-list> | <identifier> <flat-list> <record-inheritance>

<flat-list> ::= <leftcurly> <flat-list-values> <rightcurly>

<flat-list-values> ::= <flat-list-single> | <flat-list-single> <flat-list-values>

<flat-list-single> ::= <flat-name-type> <flat-operator> <flat-value> <semicolon>

<flat-operator> ::= <equals> | <plusequals>

<flat-name-type> ::= <identifier> | <identifier> <identifier> | <keyword-fk> <leftangle> <identifier> <rightangle> <identifier>

<flat-value> ::= <string> | <number> | <boolean> | <identifier> | <flat-vector> | <flat-value-list> | <flat-list>

<boolean> ::= <keyword-true> | <keyword-false>

<flat-value-list> ::= <leftsquare> <flat-value-list-value> <rightsquare>

<flat-value-list-value> ::= <flat-list-single> | <flat-list-single> <comma> <flat-value-list-value>

EBNF:

<root> = [<package-declaration>] , [<using-declaration>] , [{ <record> }], [{ <flat-definition> }] EOF

<package-declaration> ::= <keyword-package> <identifier>

<using-declaration> ::= <keyword-using> <package-list>

<package-list> ::= <identifier> | <identifier> <comma> <package-list>

<record> = <identifier> , [ <flat-inheritance> ] , <flat-list> , [ <flat-inheritance> ]

<flat-inheritance> = <semicolon> , <identifier>

<flat-list> = <leftcurly> , { <flat-definition> } , <rightcurly>

<flat-list-foreignkey> = <leftcurly> , { <flat-definition } , <rightcurly> , [ <flat-inheritance> ]

<flat-definition> = <special-tag> | [ <flat-type> ] , <identifier> , <flat-operator> , <flat-value> , <semicolon>

<special-tag> = <leftsquare> <identifier> <rightsquare>
 
<flat-type> = <flat-single-type> , { <leftsquare> <rightsquare> }

<flat-single-type> = <keyword-fk> , <leftangle> , <identifier> , <rightangle> | <keyword-int> | <keyword-string> | <keyword-bool> | <keyword-float> | <keyword-ALLTHEOTHERS>

<flat-operator> = <equals> | <plusequals>

<flat-value> = <string> | <number> | <boolean> | <identifier> | <vector> | <flat-list> [ <record-inheritance> ] | <flat-value-list>

<number> = <number-float> | <number-integer>

<boolean-value> = <keyword-true> | <keyword-false>

<flat-value-list> = <leftsquare> , <flat-value-list-value> , <rightsquare>

<flat-value-list-value> = <flat-value> | <flat-value> , <comma> , <flat-value-list-value>

Guide: https://craftinginterpreters.com/parsing-expressions.html

Uses PEG, which is not too different from BNF.  The main difference is that alternative matches (those denoted by '|') are evaluated in order, where earlier definitions take precedence over later ones.  This (I'm told) adds determinism to PEG, as oppsed to "context-free grammars" like BNF.

TODO: Find out how the tweakxl code dealt with lists, or at least the fact that there is no obvious terminator for using-keyword lists other than EOL
 
*/
namespace TweakParser
{
    public class ParserException : Exception
    {
        public ParserException(string message) : base(message) { }
    }

    public class Parser
    {
        protected TokenReader _tokenReader;

        public Parser(TokenReader tokenReader) 
        {
            this._tokenReader = tokenReader;
        }

        protected bool Match(string tokenType)
        {
            if (!_tokenReader.HasNext())
            {
                return false;
            }
            return _tokenReader.Peek()!.Type == tokenType;
        }

        protected bool Match(List<string> tokenTypes)
        {
            if (!_tokenReader.HasNext())
            {
                return false;
            }
            var nextToken = _tokenReader.Peek()!;
            return (tokenTypes.FirstOrDefault(tt => tt.Equals(nextToken.Type)) != null);
        }

        public SyntaxNode Parse()
        {
            try
            {
                return ParseRoot();
            }
            catch (TokenReaderException tre)
            {
                throw new ParserException(string.Format("TokenReaderException - {0}", tre.Message));
            }
        }

        public SyntaxNode ParseRoot()
        {
            var rootNode = new SyntaxNode("root");

            _tokenReader.Reset();
            var nextToken = _tokenReader.Peek();
            bool packageFound = false;
            while (nextToken.Type != "EOF")
            {
                // Console.WriteLine("Top level " + nextToken.ToString());
                var packageNode = ParsePackage();
                if (packageNode is not null)
                {
                    if (packageFound)
                    {
                        throw new ParserException("Cannot specified two base packages!");
                    }
                    rootNode.AddChild(packageNode);
                }
                else
                {
                    var usingNode = ParseUsingDeclaration();
                    if (usingNode is not null)
                    {
                        rootNode.AddChild(usingNode);
                    }
                    else
                    {
                        var recordDeclaration = ParseRecord();
                        if (recordDeclaration is not null)
                        {
                            rootNode.AddChild(recordDeclaration);
                        }
                        else
                        {
                            var flatDefinition = ParseFlatDefinition();

                            if (flatDefinition is not null)
                            {
                                rootNode.AddChild(flatDefinition);
                            }
                            else
                            {
                                throw new ParserException(string.Format("Unexpected token at top level - {0} : {1}", nextToken.Type, nextToken.Value));
                            }
                        }
                    }
                }
                nextToken = _tokenReader.Peek();
            }
            return rootNode;
        }

        protected SyntaxNode? ParsePackage()
        {
            if (!Match("keyword-package"))
            {
                return null;
            }

            var keywordToken = _tokenReader.Consume();
            var packageNameToken = _tokenReader.ConsumeExpected("identifier");
            return new SyntaxNode([keywordToken, packageNameToken], "package", packageNameToken.Value);
        }

        protected SyntaxNode? ParseUsingDeclaration()
        {
            if (!Match("keyword-using"))
            {
                return null;
            }

            var keywordToken = _tokenReader.Consume();
            var usingDeclarationNode = new SyntaxNode([keywordToken], "using-declaration", "");

            var packageNodes = ParsePackageList();

            if (packageNodes is null)
            {
                throw new ParserException("Keyword 'using' must be followed by one or more packages!");
            }

            usingDeclarationNode.AddChildren(packageNodes);
            return usingDeclarationNode;
        }

        protected List<SyntaxNode>? ParsePackageList()
        {
            if (!Match("identifier"))
            {
                // Console.WriteLine("ParsePackageList: ", _tokenReader.Peek().Type);
                return null;
            }

            var packageNameToken = _tokenReader.Consume();
            var packageNameNode = new SyntaxNode([packageNameToken], "package-name", packageNameToken.Value);
            var packageList = new List<SyntaxNode>([packageNameNode]);

            if (Match("comma"))
            {
                // We don't need to store the comma tokens
                _tokenReader.Consume();
                var otherPackages = ParsePackageList();
                if (otherPackages is null)
                {
                    // Unsure "where" to handle this exactly... Here seems best?
                    throw new ParserException("Expecting more package names following comma!");
                }
                else
                {
                    packageList.AddRange(otherPackages);
                }
            }
            return packageList;
        }

        protected SyntaxNode? ParseRecord()
        {
            // <record> = <identifier> , [ <record-inheritance> ] , <record-flat-list> , [ <record-inheritance> ]

            if (!Match("identifier"))
            {
                return null;
            }

            var recordNameToken = _tokenReader.Consume();
            var recordSyntaxNode = new SyntaxNode([recordNameToken], "record", recordNameToken.Value);
            var flatInheritance = ParseRecordInheritance();

            if (flatInheritance is not null)
            {
                recordSyntaxNode.AddChild(flatInheritance);
            }

            var recordFlatList = ParseFlatList();
            if (recordFlatList is null)
            {
                throw new ParserException("Records must have an associated list of flats between curly braces!");
            }
            recordSyntaxNode.AddChild(recordFlatList);

            var secondFlatInheritance = ParseRecordInheritance();
            if (secondFlatInheritance is not null)
            {
                recordSyntaxNode.AddChild(secondFlatInheritance);
            }

            return recordSyntaxNode;
        }

        protected SyntaxNode? ParseRecordInheritance()
        {
            if (!Match("colon"))
            {
                return null;
            }

            var colonToken = _tokenReader.Consume();
            var parentRecordToken = _tokenReader.ConsumeExpected("identifier");

            return new SyntaxNode([colonToken, parentRecordToken], "record-inheritance", parentRecordToken.Value);
        }

        protected SyntaxNode? ParseFlatList()
        {
            /*
                <flat-list> = <leftcurly> , { <flat-definition> } , <rightcurly>

                <flat-definition> = <identifier> , <flat-operator> , <flat-value> , <semicolon> | <identifier> , <identifier> , <flat-operator> , <flat-value> , <semicolon> | <keyword-fk> , <leftangle> , <identifier> , <rightangle> , <equals> , <string> , <semicolon>
 
                <flat-operator> = <equals> | <plusequals>

                <flat-value> = <string> | <number> | <boolean> | <identifier> | <vector> | <flat-list> [ <record-inheritance> ] | <flat-value-list>

                <boolean> = <keyword-true> | <keyword-false>

                <vector> = <leftbrack> <number> <comma> <number> <comma> <number> <rightbracket>

                <flat-value-list> = <leftsquare> , <flat-value-list-value> , <rightsquare>

                <flat-value-list-value> = <flat-value> | <flat-value> , <comma> , <flat-value-list-value>
            */

            if (!Match("leftcurly"))
            {
                return null;
            }
            var leftcurlyToken = _tokenReader.Consume();
            var definitionNodes = new List<SyntaxNode>();
            while (!Match("rightcurly"))
            {
                if (Match("EOF"))
                {
                    throw new ParserException("Unexpected end of file in flat definition list - expecting right curly brace '}'");
                }

                var flatDefinitionNode = ParseFlatDefinition();
                if (flatDefinitionNode is not null)
                {
                    definitionNodes.Add(flatDefinitionNode);
                }
                else
                {
                    throw new ParserException(string.Format("Unexpected token {0} : {1} in flat definition list", _tokenReader.Peek().Type, _tokenReader.Peek().Value));
                }
            }
            var rightcurlyToken = _tokenReader.ConsumeExpected("rightcurly");

            var flatListNode = new SyntaxNode([leftcurlyToken, rightcurlyToken], "flat-list", "");
            flatListNode.AddChildren(definitionNodes);
            return flatListNode;
        }

        protected SyntaxNode? ParseFlatDefinition()
        {
            var episodeTagNode = ParseSpecialTag();
            if (episodeTagNode is not null)
            {
                return episodeTagNode;
            }

            var flatTypeNode = ParseFlatType();

            if (Match("identifier"))
            {
                var flatNameToken = _tokenReader.Consume();
                if (Match(["equals", "plusequals"]))
                {
                    // identifier without type
                    var operatorToken = _tokenReader.Consume();
                    var flatValueNode = ParseFlatValue();
                    var semicolonToken = _tokenReader.ConsumeExpected("semicolon");

                    if (flatValueNode is null)
                    {
                        throw new ParserException("Unable to parse value for flat definition");
                    }

                    var definitionNode = new SyntaxNode([flatNameToken, operatorToken, semicolonToken], "flat-definition", "");
                    definitionNode.AddChild(new SyntaxNode("name", flatNameToken.Value));
                    if (flatTypeNode is not null)
                    {
                        definitionNode.AddChild(flatTypeNode);
                    }
                    definitionNode.AddChild(new SyntaxNode("operation", operatorToken.Type == "equals" ? "assign" : "append"));
                    definitionNode.AddChild(flatValueNode);
                    return definitionNode;
                }
                else
                {
                    throw new ParserException("Expecting assignment/append operator in flat definition ('=' or '+=')");
                }
            }
            return null;
        }

        protected SyntaxNode? ParseFlatValue()
        {
            // <flat-value> = <string> | <number> | <boolean> | <identifier> | <vector> | <flat-list> [ <record-inheritance> ] | <flat-value-list>

            if (Match("string"))
            {
                var stringToken = _tokenReader.Consume();
                return new SyntaxNode([stringToken], "value-string", stringToken.Value);
            }
            else if (Match("number-integer"))
            {
                var numberToken = _tokenReader.Consume();
                return new SyntaxNode([numberToken], "value-number-integer", numberToken.Value);
            }
            else if (Match("number-float"))
            {
                var numberToken = _tokenReader.Consume();
                return new SyntaxNode([numberToken], "value-number-float", numberToken.Value);
            }
            else if (Match(["keyword-true", "keyword-false"]))
            {
                var booleanToken = _tokenReader.Consume();
                return new SyntaxNode([booleanToken], "value-boolean", booleanToken.Type.Substring(8));
            }
            else if (Match(["identifier"]))
            {
                var identifierToken = _tokenReader.Consume();
                return new SyntaxNode([identifierToken], "value-identifier", identifierToken.Value);
            }
            else
            {
                var inlineFlatList = ParseFlatList();
                if (inlineFlatList is not null)
                {
                    var inlineValueNode = new SyntaxNode("value-inline", "");
                    inlineValueNode.AddChild(inlineFlatList);
                    var inlineInheritanceNode = ParseRecordInheritance();
                    if (inlineInheritanceNode is not null)
                    {
                        inlineValueNode.Type = "value-foreignkey";
                        inlineValueNode.Value = inlineInheritanceNode.Value;
                        inlineValueNode.AddChild(inlineInheritanceNode);
                    }
                    return inlineValueNode;
                }

                var vectorValue = ParseVector();
                if (vectorValue is not null)
                {
                    return vectorValue;
                }

                var flatListValue = ParseFlatValueList();
                if (flatListValue is not null)
                {
                    return flatListValue;
                }

                return null;
            }
        }

        protected SyntaxNode? ParseFlatType()
        {
            var typePrimitiveNode = ParseFlatSingleType();

            if (typePrimitiveNode is null)
            {
                return null;
            }

            if (Match("leftsquare"))
            {
                var leftsquareToken = _tokenReader.Consume();
                var rightsquareToken = _tokenReader.ConsumeExpected("rightsquare");

                var arrayTypeNode = new SyntaxNode([ leftsquareToken, rightsquareToken ], "flat-type", "array");
                arrayTypeNode.AddChild(typePrimitiveNode);
                return arrayTypeNode;
            }
            return typePrimitiveNode;
        }

        protected SyntaxNode? ParseFlatSingleType()
        {
            if (Match("keyword-fk"))
            {
                var fkKeywordToken = _tokenReader.Consume();
                var leftangleToken = _tokenReader.ConsumeExpected("leftangle");
                var fkTypeToken = _tokenReader.ConsumeExpected("identifier");
                var rightangleToken = _tokenReader.ConsumeExpected("rightangle");

                var flatTypeNode = new SyntaxNode("flat-type", "foreignkey");
                flatTypeNode.AddChild(new SyntaxNode("foreignkey-type", fkTypeToken.Value));
                return flatTypeNode;
            }
            if (Match([ "keyword-string", "keyword-int", "keyword-bool", "keyword-float", "keyword-CName", "keyword-Vector2", "keyword-Vector3", "keyword-LocKey", "keyword-ResRef", "keyword-EulerAngles", "keyword-Quaternion" ]))
            {
                var typeToken = _tokenReader.Consume();
                return new SyntaxNode([ typeToken ], "flat-type", typeToken.Type.Substring(8));
            }

            return null;
        }

        protected SyntaxNode? ParseVector()
        {
            if (!Match("leftbracket"))
            {
                return null;
            }

            List<string> acceptableNumberTokenTypes = ["number-float", "number-integer"];

            var leftbracketToken = _tokenReader.Consume();
            var xToken = _tokenReader.ConsumeExpectedChoice(acceptableNumberTokenTypes);
            _tokenReader.ConsumeExpected("comma");
            var yToken = _tokenReader.ConsumeExpectedChoice(acceptableNumberTokenTypes);
            Token? zToken = null;
            Token? aToken = null; // For quaternions

            // TODO: This should be done using proper parsing, not that I'm expecting five-valued vectors
            if (Match("comma"))
            {
                _tokenReader.ConsumeExpected("comma");
                zToken = _tokenReader.ConsumeExpectedChoice(acceptableNumberTokenTypes);
            }
            if (Match("comma"))
            {
                _tokenReader.ConsumeExpected("comma");
                aToken = _tokenReader.ConsumeExpectedChoice(acceptableNumberTokenTypes);
            }
            var rightbracketToken = _tokenReader.ConsumeExpected("rightbracket");

            var vectorNode = new SyntaxNode([leftbracketToken, xToken, yToken], "value-vector", string.Format("{0} {1}", xToken.Value, yToken.Value));
            vectorNode.AddChild(new SyntaxNode("x", xToken.Value));
            vectorNode.AddChild(new SyntaxNode("y", yToken.Value));

            if (zToken is not null)
            {
                vectorNode.GetTokenData()!.Add(zToken);
                vectorNode.Type = "value-vector3";
                vectorNode.Value += string.Format(" {0}", zToken.Value);
                vectorNode.AddChild(new SyntaxNode("z", zToken.Value));
            }
            if (aToken is not null)
            {
                vectorNode.GetTokenData()!.Add(aToken);
                vectorNode.Type = "value-vector4";
                vectorNode.Value += string.Format(" {0}", aToken.Value);
                vectorNode.AddChild(new SyntaxNode("a", aToken.Value));
            }
            return vectorNode;
        }

        protected SyntaxNode? ParseFlatValueList()
        {
            /*
                <flat-value-list> = <leftsquare> , [ <flat-value-list-value> ], <rightsquare>

                <flat-value-list-value> = <flat-value> | <flat-value> , <comma> , <flat-value-list-value>
             */
            if (!Match("leftsquare"))
            {
                return null;
            }

            var leftbracketToken = _tokenReader.Consume();

            var flatValues = ParseFlatValueListValue();

            var rightbracketToken = _tokenReader.ConsumeExpected("rightsquare");

            var valueListNode = new SyntaxNode([leftbracketToken, rightbracketToken], "value-list", "");

            if (flatValues is not null)
            {
                valueListNode.AddChildren(flatValues);
            }
            return valueListNode;
        }

        protected List<SyntaxNode>? ParseFlatValueListValue()
        {
            var flatValue = ParseFlatValue();

            if (flatValue is null)
            {
                return null;
            }

            var result = new List<SyntaxNode>([flatValue]);

            if (Match("comma"))
            {
                var commaToken = _tokenReader.Consume();
                var nextValues = ParseFlatValueListValue();
                if (nextValues is null)
                {
                    throw new ParserException("Expecting more values following comma in flat value list");
                }
                result.AddRange(nextValues);
            }

            return result;
        }

        protected SyntaxNode? ParseSpecialTag()
        {
            if (Match("leftsquare"))
            {
                var leftsquareToken = _tokenReader.Consume();
                var tagNameToken = _tokenReader.ConsumeExpected("identifier");
                var rightsquareToken = _tokenReader.ConsumeExpected("rightsquare");

                return new SyntaxNode([leftsquareToken, tagNameToken, rightsquareToken], "special-tag", tagNameToken.Value);
            }
            return null;
        }
    }
}
