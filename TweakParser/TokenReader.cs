using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TweakParser
{
    public class TokenReaderException : Exception
    {
        public TokenReaderException(string message) : base(message) { }
    }

    public class TokenReader
    {
        protected List<Token> _tokens;
        protected int _token_index;

        public TokenReader(List<Token> tokens)
        {
            _tokens = tokens;
            _token_index = 0;
        }

        public void Reset()
        {
            _token_index = 0;
        }

        public Token Consume()
        {
            // Remember - last token should be EOF, and reader should sit here indefinitely
            if (_token_index < _tokens.Count - 1) 
            {
                return _tokens[_token_index++];
            }
            else if (_tokens.Count > 0)
            {
                return _tokens.Last();
            }
            else
            {
                // Empty token list
                return new Token("ListEmpty", "");
            }
        }

        public Token ConsumeExpected(string tokenType)
        {
            if (Peek().Type != tokenType)
            {
                throw new TokenReaderException(string.Format("Expecting '{0}' token type", tokenType));
            }
            else
            {
                return Consume();
            }
        }

        public Token ConsumeExpectedChoice(List<string> tokenTypes)
        {
            var nextToken = Peek();
            var contains = tokenTypes.Any(x => x.Equals(nextToken));
            if (contains)
            {
                throw new TokenReaderException(string.Format("Expecting token of types [{0}]", string.Join(',', tokenTypes)));
            }
            else
            {
                return Consume();
            }
        }

        public Token Previous()
        {
            if (_token_index > 0)
            {
                return _tokens[_token_index - 1];
            }
            else if (_tokens.Count == 0)
            {
                return new Token("ListEmpty", "");
            }
            else
            { 
                return new Token("AtFirst", "");
            }
        }

        public Token Peek()
        {
            return _tokens[_token_index];
        }

        public bool HasNext()
        {
            return _token_index < _tokens.Count - 1;
        }

    }
}
