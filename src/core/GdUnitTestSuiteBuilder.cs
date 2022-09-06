using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GdUnit3.Core
{
    public class GdUnitTestSuiteBuilder
    {

        private static Dictionary<String, Type> clazzCache = new Dictionary<string, Type>();

        public static Dictionary<string, object> Build(string sourcePath, int lineNumber, string testSuitePath)
        {
            var result = new Dictionary<string, object>();
            result.Add("path", testSuitePath);
            try
            {
                Type? type = ParseType(sourcePath);
                if (type == null)
                {
                    result.Add("error", $"Can't parse class type from {sourcePath}:{lineNumber}.");
                    return result;
                }
                string methodToTest = FindMethod(sourcePath, lineNumber) ?? "";
                if (String.IsNullOrEmpty(methodToTest))
                {
                    result.Add("error", $"Can't parse method name from {sourcePath}:{lineNumber}.");
                    return result;
                }

                // create directory if not exists
                var fi = new FileInfo(testSuitePath);
                if (!fi.Exists)
                    System.IO.Directory.CreateDirectory(fi.Directory.FullName);

                if (!File.Exists(testSuitePath))
                {
                    string template = FillFromTemplate(LoadTestSuiteTemplate(), type, sourcePath);
                    SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(template);
                    //var toWrite = syntaxTree.WithFilePath(testSuitePath).GetCompilationUnitRoot();
                    var toWrite = AddTestCase(syntaxTree, methodToTest);

                    using (StreamWriter streamWriter = File.CreateText(testSuitePath))
                    {
                        toWrite.WriteTo(streamWriter);
                    }
                    result.Add("line", TestCaseLineNumber(toWrite, methodToTest));
                }
                else if (methodToTest != null)
                {
                    SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(File.ReadAllText(testSuitePath));
                    var toWrite = syntaxTree.WithFilePath(testSuitePath).GetCompilationUnitRoot();
                    if (TestCaseExists(toWrite, methodToTest))
                    {
                        result.Add("line", TestCaseLineNumber(toWrite, methodToTest));
                        return result;
                    }
                    toWrite = AddTestCase(syntaxTree, methodToTest);
                    using (StreamWriter streamWriter = File.CreateText(testSuitePath))
                    {
                        toWrite.WriteTo(streamWriter);
                    }
                    result.Add("line", TestCaseLineNumber(toWrite, methodToTest));
                }
                return result;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Can't parse method name from {sourcePath}:{lineNumber}. Error: {e.Message}");
                result.Add("error", e.Message);
                return result;
            }
        }

        public static Type? ParseType(String classPath)
        {
            if (String.IsNullOrEmpty(classPath) || !new FileInfo(classPath).Exists)
            {
                Console.Error.WriteLine($"Class `{classPath}` not exists .");
                Godot.GD.PrintS("Build Error", $"Class `{classPath}` not exists .");
                return null;
            }
            try
            {
                var root = CSharpSyntaxTree.ParseText(File.ReadAllText(classPath)).GetCompilationUnitRoot();
                NamespaceDeclarationSyntax namespaceSyntax = root.Members.OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
                if (namespaceSyntax != null)
                {
                    ClassDeclarationSyntax classSyntax = namespaceSyntax.Members.OfType<ClassDeclarationSyntax>().First();
                    return FindTypeOnAssembly(namespaceSyntax.Name.ToString() + "." + classSyntax.Identifier.ValueText);
                }
                ClassDeclarationSyntax programClassSyntax = root.Members.OfType<ClassDeclarationSyntax>().First();
                return FindTypeOnAssembly(programClassSyntax.Identifier.ValueText);
            }
#pragma warning disable CS0168
            catch (Exception e)
            {
                Console.Error.WriteLine($"Can't parse namespace of {classPath}. Error: {e.Message}");
                Godot.GD.PrintS("Build Error", $"Can't parse namespace of {classPath}. Error: {e.Message}");
#pragma warning restore CS0168
                // ignore exception
                return null;
            }
        }

        public static Godot.Node? ParseTestSuite(string classPath)
        {
            if (String.IsNullOrEmpty(classPath) || !new FileInfo(classPath).Exists)
            {
                Console.Error.WriteLine($"Parse Error: Class `{classPath}` not exists .");
                Godot.GD.PrintS("Parse Error:", $"Class `{classPath}` not exists .");
                return null;
            }
            try
            {
                Type? type = GdUnitTestSuiteBuilder.ParseType(classPath);
                if (type == null)
                    return null;
                var testSuite = new CsNode(type.Name, classPath);

                SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(File.ReadAllText(classPath));
                ClassDeclarationSyntax programClassSyntax = GdUnitTestSuiteBuilder.ClassDeclaration(syntaxTree.GetCompilationUnitRoot());
                var compilationUnit = syntaxTree.WithFilePath(classPath).GetCompilationUnitRoot();


                type.GetMethods()
                    .Where(mi => mi.IsDefined(typeof(TestCaseAttribute)))
                    .Select(mi =>
                    {
                        var lineNumber = GdUnitTestSuiteBuilder.TestCaseLineNumber(compilationUnit, mi.Name);
                        return new GdUnit3.Executions.TestCase(mi, lineNumber);
                    })
                    .ToList()
                    .ForEach(testCase => testSuite.AddChild(new CsNode(testCase.Name, classPath, testCase.Line)));
                return testSuite;
            }
#pragma warning disable CS0168
            catch (Exception e)
            {
#pragma warning restore CS0168
                // ignore exception
                return null;
            }
        }

        private static Type? FindTypeOnAssembly(String clazz)
        {
            if (clazzCache.ContainsKey(clazz))
            {
                // Godot.GD.PrintS("Use class cache");
                return clazzCache[clazz];
            }
            Type? type = Type.GetType(clazz);
            if (type != null)
                return type;
            // if the class not found on current assembly lookup over all ohter loaded assemblies
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(clazz);
                if (type != null)
                {
                    // Godot.GD.PrintS("found on assemblyName", $"Name={name.Name} Version={name.Version} Location={assembly.Location}");
                    clazzCache.Add(clazz, type);
                    return type;
                }
            }
            return null;
        }


        private static string LoadTestSuiteTemplate()
        {
            if (Godot.ProjectSettings.HasSetting("gdunit3/templates/testsuite/CSharpScript"))
                return (string)Godot.ProjectSettings.GetSetting("gdunit3/templates/testsuite/CSharpScript");
            var script = Godot.ResourceLoader.Load("res://addons/gdUnit3/src/core/templates/test_suite/GdUnitTestSuiteDefaultTemplate.gd");
            return (string)script.Get("DEFAULT_TEMP_TS_CS");
        }

        private const string TAG_TEST_SUITE_NAMESPACE = "${name_space}";
        private const string TAG_TEST_SUITE_CLASS = "${suite_class_name}";
        private const string TAG_SOURCE_CLASS_NAME = "${source_class}";
        private const string TAG_SOURCE_CLASS_VARNAME = "${source_var}";
        private const string TAG_SOURCE_RESOURCE_PATH = "${source_resource_path}";


        private static string FillFromTemplate(string template, Type type, string classPath) =>
            template
                .Replace(TAG_TEST_SUITE_NAMESPACE, String.IsNullOrEmpty(type.Namespace) ? "GdUnitDefaultTestNamespace" : type.Namespace)
                .Replace(TAG_TEST_SUITE_CLASS, type.Name + "Test")
                .Replace(TAG_SOURCE_RESOURCE_PATH, classPath)
                .Replace(TAG_SOURCE_CLASS_NAME, type.Name)
                .Replace(TAG_SOURCE_CLASS_VARNAME, type.Name);

        internal static ClassDeclarationSyntax ClassDeclaration(CompilationUnitSyntax root)
        {
            NamespaceDeclarationSyntax namespaceSyntax = root.Members.OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
            return namespaceSyntax == null
                ? root.Members.OfType<ClassDeclarationSyntax>().First()
                : namespaceSyntax.Members.OfType<ClassDeclarationSyntax>().First();
        }

        internal static int TestCaseLineNumber(CompilationUnitSyntax root, string testCaseName)
        {
            // lookup on test cases
            return ClassDeclaration(root).Members.OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(method => method.Identifier.Text.Equals(testCaseName))
                .Body?.GetLocation().GetLineSpan().StartLinePosition.Line ?? -1;
        }

        internal static bool TestCaseExists(CompilationUnitSyntax root, string testCaseName) =>
            ClassDeclaration(root).Members.OfType<MethodDeclarationSyntax>().Any(method => method.Identifier.Text.Equals(testCaseName));

        internal static CompilationUnitSyntax AddTestCase(SyntaxTree syntaxTree, string testCaseName)
        {
            var root = syntaxTree.GetCompilationUnitRoot();
            ClassDeclarationSyntax programClassSyntax = ClassDeclaration(root);
            SyntaxNode insertAt = programClassSyntax.ChildNodes().Last()!;

            AttributeSyntax testCaseAttribute = SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("TestCase"));
            AttributeListSyntax attributes = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList<AttributeSyntax>(testCaseAttribute));

            MethodDeclarationSyntax method = SyntaxFactory.MethodDeclaration(
                SyntaxFactory.List<AttributeListSyntax>().Add(attributes),
                SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)),
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                default(ExplicitInterfaceSpecifierSyntax),
                SyntaxFactory.Identifier(testCaseName),
                default(TypeParameterListSyntax),
                SyntaxFactory.ParameterList(),
                default(SyntaxList<TypeParameterConstraintClauseSyntax>),
                SyntaxFactory.Block(),
                default(ArrowExpressionClauseSyntax),
                default(SyntaxToken));

            BlockSyntax newBody = SyntaxFactory.Block(SyntaxFactory.ParseStatement("AssertNotYetImplemented();"));
            method = method.ReplaceNode(method.Body!, newBody);
            return root.InsertNodesAfter(insertAt, new[] { method }).NormalizeWhitespace("\t", "\n");
        }

        internal static string? FindMethod(string sourcePath, int lineNumber)
        {
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(File.ReadAllText(sourcePath));
            ClassDeclarationSyntax programClassSyntax = ClassDeclaration(syntaxTree.GetCompilationUnitRoot());
            if (programClassSyntax == null)
            {
                Console.Error.WriteLine($"Can't parse method name from {sourcePath}:{lineNumber}. Error: no class declararion found.");
                return null;
            }

            var spanToFind = syntaxTree.GetText().Lines[lineNumber - 1].Span;
            // lookup on properties
            foreach (PropertyDeclarationSyntax m in programClassSyntax.Members.OfType<PropertyDeclarationSyntax>())
            {
                if (m.FullSpan.IntersectsWith(spanToFind))
                    return m.Identifier.Text;
            }
            // lookup on methods
            foreach (MethodDeclarationSyntax m in programClassSyntax.Members.OfType<MethodDeclarationSyntax>())
            {
                if (m.FullSpan.IntersectsWith(spanToFind))
                    return m.Identifier.Text;
            }
            return null;
        }

    }
}
