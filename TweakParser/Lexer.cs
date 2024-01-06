using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TweakParser
{
    public class LexerException : Exception
    {
        public LexerException(string message) : base(message) { }
    }


    public class Lexer
    {
        public Lexer() { }

        public List<string> Keywords = ["true", "false", "package", "using", "fk", "bool", "string", "int", "float", "CName", "Vector2", "Vector3", "LocKey", "ResRef", "EulerAngles", "Quaternion" ];

        public List<Token> Lex(string s)
        {
            List<Token> tokens = new List<Token>();

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];

                if (c.Equals('{'))
                {
                    tokens.Add(new Token("leftcurly", c.ToString()));
                }
                else if (c.Equals('}'))
                {
                    tokens.Add(new Token("rightcurly", c.ToString()));
                }
                else if (c == ':')
                {
                    tokens.Add(new Token("colon", c.ToString()));
                }
                else if (c == ',')
                {
                    tokens.Add(new Token("comma", c.ToString()));
                }
                else if (c.Equals(';'))
                {
                    tokens.Add(new Token("semicolon", c.ToString()));
                }
                else if (c.Equals('='))
                {
                    tokens.Add(new Token("equals", c.ToString()));
                }
                else if (c.Equals('['))
                {
                    tokens.Add(new Token("leftsquare", c.ToString()));
                }
                else if (c.Equals(']'))
                {
                    tokens.Add(new Token("rightsquare", c.ToString()));
                }
                else if (c.Equals('<'))
                {
                    tokens.Add(new Token("leftangle", c.ToString()));
                }
                else if (c.Equals('>'))
                {
                    tokens.Add(new Token("rightangle", c.ToString()));
                }
                else if (c.Equals('('))
                {
                    tokens.Add(new Token("leftbracket", c.ToString()));
                }
                else if (c.Equals(')'))
                {
                    tokens.Add(new Token("rightbracket", c.ToString()));
                }
                else if (c.Equals('+'))
                {
                    if (i < s.Length - 1 && s[i + 1].Equals('='))
                    {
                        tokens.Add(new Token("plusequals", "+="));
                        i++;
                    }
                    else
                    {
                        throw new LexerException(string.Format("'+' only recognised as part of append operator '+=' (index position: {0})", i));
                    }
                }
                else if ("-0123456789.".Contains(c))
                {
                    StringBuilder sb = new StringBuilder();
                    bool decimalFound = (c == '.');
                    bool typeSpecifierFound = false;
                    string typeSpecifiers = "f";
                    string validNumberChars = "0123456789." + typeSpecifiers;
                    int j = i + 1;

                    sb.Append(c);
                    for (; j < s.Length; j++)
                    {
                        if (validNumberChars.Contains(s[j]))
                        {
                            if (s[j] == '.')
                            {
                                if (decimalFound)
                                {
                                    throw new LexerException(string.Format("Number cannot contain two decimal points (index position: {0})", j));
                                }
                                else
                                {
                                    sb.Append(s[j]);
                                    decimalFound = true;
                                }
                            }
                            else if (typeSpecifiers.Contains(s[j]))
                            {
                                if (typeSpecifierFound)
                                {
                                    throw new LexerException(string.Format("Number type specifier must occur at end of number (index position: {0})", j));
                                }
                                else
                                {
                                    sb.Append(s[j]);
                                    typeSpecifierFound = true;
                                }
                            }
                            else
                            {
                                sb.Append(s[j]);
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                    var numberString = sb.ToString();
                    if (numberString.Contains('.') || numberString.Contains("f"))
                    {
                        tokens.Add(new Token("number-float", numberString));
                    }
                    else
                    {
                        tokens.Add(new Token("number-integer", numberString));
                    }
                    i = j - 1;
                }
                else if (Char.IsLetter(c) || c == '_')
                {
                    StringBuilder sb = new StringBuilder();
                    int j = i + 1;

                    sb.Append(c);

                    for (; j < s.Length; j++)
                    {
                        if (Char.IsLetter(s[j]) || Char.IsDigit(s[j]) || s[j] == '_' || s[j] == '.')
                        {
                            sb.Append(s[j]);
                        }
                        else
                        {
                            break;
                        }
                    }
                    var identifier = sb.ToString();
                    var keywordMatch = Keywords.FirstOrDefault(kw => kw.Equals(identifier));
                    if (keywordMatch != null)
                    {
                        tokens.Add(new Token("keyword-"+identifier, ""));
                    }
                    else
                    {
                        tokens.Add(new Token("identifier", identifier));
                    }
                    i = j - 1;
                }
                else if (c.Equals('"'))
                {
                    StringBuilder sb = new StringBuilder();
                    int j = i + 1;

                    while (s[j] != '"' && j < s.Length)
                    {
                        sb.Append(s[j]);
                        j++;
                    }

                    if (s[j] == '"')
                    {
                        tokens.Add(new Token("string", sb.ToString()));
                    }
                    else
                    {
                        throw new LexerException(string.Format("Unterminated string (index position {0})", i));
                    }
                    i = j;
                }
                else if (!Char.IsWhiteSpace(c))
                {
                    throw new LexerException(string.Format("Unrecognised character '{0}' (index position {1})", c, i));
                }
            }
            tokens.Add(new Token("EOF", ""));
            return tokens;
        }
    }
}
