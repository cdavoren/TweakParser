// See https://aka.ms/new-console-template for more information
using System.Runtime.InteropServices;
using TweakParser;

namespace TweakParser;

class Program
{
    
    public static List<Tuple<string, SyntaxNode>> ProcessDirectory(string directoryPath, SemanticAnalyzer analyzer)
    {
        var files = Directory.GetFiles(directoryPath, "*.tweak");
        var rootSyntaxNodes = new List<Tuple<string, SyntaxNode>>();
        var rootSemanticNodes = new List<Tuple<string, SemanticNode>>();

        foreach (var file in files)
        {
            // Console.WriteLine(string.Format("File: {0} / {1} ...", directoryPath, file));

            var inputString = File.ReadAllText(file);
            var tweakLexer = new Lexer();
            var tokens = tweakLexer.Lex(inputString);

            var tweakParser = new Parser(new TokenReader(tokens));
            var rootNode = tweakParser.Parse();
            rootSyntaxNodes.Add(new Tuple<string, SyntaxNode>(file, rootNode));

            var rootSemanticNode = analyzer.Analyze(rootNode, file);
            // Console.WriteLine(string.Format("  - contains {0} record(s) and {1} group(s)", rootSemanticNode.GetChildren().Count(x => x is RecordSemanticNode), rootSemanticNode.GetChildren().Count(x => x is GroupSemanticNode)));
            rootSemanticNodes.Add(new Tuple<string, SemanticNode>(file, rootSemanticNode));
        }

        var directories = Directory.GetDirectories(directoryPath);

        foreach (var directrory in directories)
        {
            rootSyntaxNodes.AddRange(ProcessDirectory(directrory, analyzer));
        }
        return rootSyntaxNodes;
    }

    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Please provide the filename or directory to parse.");
            System.Environment.Exit(1);
        }

        Console.WriteLine(args[0]);

        var inputPath = args[0];

        FileAttributes attr = File.GetAttributes(inputPath);

        if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
        {
            // Console.WriteLine("Path is a directory - iterating recursively...");
            var semanticAnalyzer = new SemanticAnalyzer();
            var fileTrees = ProcessDirectory(inputPath, semanticAnalyzer);

            semanticAnalyzer.ResolveReferences();
            Console.WriteLine(string.Format("Number of trees created: {0}", fileTrees.Count));
            // Console.WriteLine("Sanity check commencing...");
            // semanticAnalyzer.SanityCheck();


            var testRecordName = "Vehicle.v_standard2_archer_quartz_player";
            // var testRecordName = "Items.crowd_int_apparel_002__umbrella_small"; // Circular references
            Console.WriteLine(string.Format("Attempting flattening of: '{0}'", testRecordName));
            var flattenedRecord = semanticAnalyzer.FlattenRecordByName(testRecordName);
            flattenedRecord.Print(string.Empty);
            return;
        }

        var inputString = File.ReadAllText(args[0]);
        var tweakLexer = new Lexer();
        List<Token>? tokens;

        try
        {
            tokens = tweakLexer.Lex(inputString);
            Console.WriteLine(string.Format("Lexing finished with {0} tokens found.", tokens.Count));
        }
        catch (LexerException le)
        {
            Console.WriteLine(string.Format("Lexer exception has occurred: {0}", le.Message));
            return;
        }

        foreach (var token in tokens)
        {
            Console.WriteLine(string.Format("  {0} : {1}", token.Type, token.Value));
        }

        var tweakParser = new Parser(new TokenReader(tokens));
        SyntaxNode rootNode;
        try
        {
            rootNode = tweakParser.Parse();

            rootNode.Print();
        }
        catch (ParserException pe)
        {
            Console.WriteLine(string.Format("Parser exception has occurred: {0}", pe.Message));
            return;
        }

        var tweakAnalyzer = new SemanticAnalyzer();
        SemanticNode rootSemanticNode;
        rootSemanticNode = tweakAnalyzer.Analyze(rootNode, inputString);
        Console.WriteLine(rootSemanticNode.GetChildren().Count);
        Console.WriteLine(string.Format("  - contains {0} record(s), {2} top-level flats", 
            rootSemanticNode.GetChildren().Count(x => x is RecordSemanticNode), 
            rootSemanticNode.GetChildren().Count(x => x is FlatSemanticNode)));
    }
}

