using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CSharp;
using System.CodeDom.Compiler;
using System.Reflection;
using System.Diagnostics;
using System.IO;
using System.Security.Policy;

namespace TestCSHosting
{

    /*
     * References (in order of relevance) : 
     * 
     * http://stackoverflow.com/questions/137933/what-is-the-best-scripting-language-to-embed-in-a-c-sharp-desktop-application
     * 
     * http://stackoverflow.com/questions/6155406/referencing-dynamically-loaded-assemblies-in-dynamically-compiled-code
     * http://www.codeproject.com/Articles/23227/Dynamic-Creation-of-Assemblies-Apps
     * 
     * 
     * 
     */

#warning change : plan to pass the ScriptingApi as a parameter of the Execute method

    // Idea : A copy of this is received by the scripting AppDomain.
    // The Invoke method is implemented just by looking in the scripting assembly by simple reflection 
    // within that same AppDomain for the required type. Just requires lookup from script name to class name.

    // Do this rather than using more AppDomain stuff which I have little knowledge of!

    [Serializable]
    public class ScriptingApi
    {
        public string f(string s)
        {
            return s.Substring(1);
        }

        //public AppDomain Domain { private get;  set; }
        //public string AssemblyPath { private get;  set; }

        // this is copied into the scripting AppDomain.

        public Dictionary<string, string> m_scriptToClass;

        // Need to pass in the lookup of script name to full class name here, and store it
        public object Invoke(string scriptName)
        {
            var c = Assembly.GetCallingAssembly();
            var e = Assembly.GetExecutingAssembly();


            Type classType = Assembly.GetCallingAssembly().GetExportedTypes().FirstOrDefault(t => t.Name == scriptName);

            ConstructorInfo ctor = classType.GetConstructor(Type.EmptyTypes);

            var impl = ctor.Invoke(null) as IScriptedMethod;
            return impl.Execute(this, new string[] { "blah blah", "adf" });

#if false
            var h2 = (IScriptedMethod)AppDomain.CurrentDomain.CreateInstanceFromAndUnwrap(AssemblyPath, "Blah.concat2");
            h2.Api = new ScriptingApi() { Domain = this.Domain, AssemblyPath = this.AssemblyPath, };

            return h2.Execute(new string[] { "blah blah" , "adf" });
#endif
        }
    }

    public interface IScriptedMethod
    {
        object Execute(ScriptingApi Api, object[] args);
    }

    class Program
    {
        /*
         * Idea : 
         *  Generate an assembly per mapping file. 
         *  Generate the namespace from the mapping file,
         * in such a fashion that it is unique.
         * 
         * Cache the compiled assembly!
         * Threadsafety?
         * 
         * Provide tool to validate code?
         * 
         * 
         * 
         * 
         */



        delegate Dictionary<string, string> ScriptsFactoryDelegate(); 

        static Dictionary<string, string> scripts1()
        {
            // args is a string[]

            // all code assumed to be of inferred signature : 
            // string f(string[] args)

            var d = new Dictionary<string, string>();
            d.Add("concat2", @"return (string)args[0] + (string)args[1];");
            d.Add("f", @"return Api.f(args[0] as string);");
            d.Add("testmethod", @"return ""1"";");
            d.Add("gettuple", @"return Tuple.Create<string, int>(""2"", 3);");
            d.Add("invoker", @"return Api.Invoke(""gettuple"");");

            return d;
        }

        static Dictionary<string, string> scripts2()
        {
            // args is a string[]

            // all code assumed to be of inferred signature : 
            // string f(string[] args)

            var d = new Dictionary<string, string>();
            d.Add("concat2", @"return (string)args[0] + (string)args[1];");
            d.Add("testmethod", @"return ""2"";");
            d.Add("gettuple", @"return Tuple.Create<string, int>(""2"", 3);");

            return d;
        }





        static string implement(Dictionary<string, string> d)
        {
            var s = new StringBuilder();
            s.AppendLine("using System;");
            // Important! Need to reference the interface!!            
            s.AppendLine("using TestCSHosting;");
            s.AppendLine();
            s.AppendLine(
                "namespace Blah" +
                "{"
                );

            bool byRef = true;

            foreach (var kvp in d)
            {
                string classInfo =
                    byRef
                    ?
                    "public class " + kvp.Key + " : MarshalByRefObject, IScriptedMethod"
                    :
                    "[Serializable]public class " + kvp.Key + " : IScriptedMethod";


                s.AppendLine(string.Format(
                    classInfo +
                    "{{" +
                    "" + 
                    "   public object Execute(ScriptingApi Api, object[] args)" +
                    "   {{" +
                    "       {0}" +
                    "   }}" +
                    "}}"
                    , kvp.Value
                    )
                    );
            }


            s.Append(
                "}"
                );

            return s.ToString();
        }

        static string compile(string unit)
        {
            var csProvider = new Microsoft.CSharp.CSharpCodeProvider();

            var options = new CompilerParameters();
            options.GenerateExecutable = false;
            // GenerateInMemory = true does the same thing as = false - generates an assembly on disk as a dll.
            // but then goes on to load it into *this* appdomain.
            options.GenerateInMemory = false;

            // Important! Need to reference the interface, wherever it is!!
            options.ReferencedAssemblies.Add("System.Core.dll");
            options.ReferencedAssemblies.Add("System.dll");
            options.ReferencedAssemblies.Add(Assembly.GetExecutingAssembly().Location);

            CompilerResults result;
            result = csProvider.CompileAssemblyFromSource(options, unit);

            if (result.Errors.HasErrors)
            {
                foreach (CompilerError e in result.Errors)
                {
                    Console.WriteLine(e.ToString());
                }

                throw new Exception("Compiler error!");
            }

            if (result.Errors.HasWarnings)
            {
                foreach (CompilerError e in result.Errors)
                {
                    Console.WriteLine(e.ToString());
                }
            }

            return result.PathToAssembly;
        }

        // Generate an assembly with the compiled scripts.
        // Write to disk as a dll. DOES NOT LOAD THAT ASSEMBLY INTO
        // THE CURRENT APPDOMAIN
        static string writeScriptAssembly(ScriptsFactoryDelegate getScripts)
        {
            var scripts = getScripts();
            string unit = implement(scripts);
            return compile(unit);
        }



        //http://stackoverflow.com/questions/658498/how-to-load-assembly-to-appdomain-with-all-references-recursively

        //http://msdn.microsoft.com/en-us/library/hd2zs67y(v=vs.110).aspx

        static void Main(string[] args)
        {
            try
            {
                string assemblyPath = writeScriptAssembly(scripts1);

                //byte[] assemblyFile = File.ReadAllBytes(assemblyPath);
                //var a = Assembly.Load(assemblyFile);
                // we can clean up after ourselves (housekeep) [at some point]
                // File.Delete(result.PathToAssembly);

                //var b = Assembly.Load(result.PathToAssembly);
                var ads = new AppDomainSetup();
                //ads.ApplicationBase = System.Environment.CurrentDirectory;
                //ads.ApplicationBase = AppDomain.CurrentDomain.BaseDirectory;
                //ads.DisallowBindingRedirects = false;   // doesn't seem to affect us
                Evidence adevidence = null; // AppDomain.CurrentDomain.Evidence;
                var appDomain = AppDomain.CreateDomain("TestDomain", adevidence, ads);

                // This call will load the assembly into the appDomain.
                // http://msdn.microsoft.com/en-us/library/25y1ya39(v=vs.110).aspx
                var h = (IScriptedMethod)appDomain.CreateInstanceFromAndUnwrap(assemblyPath, "Blah.invoker");
                
                object o = h.Execute(new ScriptingApi(), new string[] { "helllllll", "lo" });


                var h2 = (IScriptedMethod)appDomain.CreateInstanceFromAndUnwrap(assemblyPath, "Blah.f");

                object o2 = h2.Execute(new ScriptingApi(), new string[]{ "blah blah" });

                AppDomain.Unload(appDomain);
                File.Delete(assemblyPath);

                // this will throw, since the appdomain has already been unloaded
                //h2.Execute(new string[] { });

                // same with these two!
                //var h3 = (IScriptedMethod)appDomain.CreateInstanceFromAndUnwrap(assemblyPath, "Blah.gettuple");
                //object o3 = h2.Execute(new string[] { });


                string assemblyPath2 = writeScriptAssembly(scripts2);

                var appDomain2 = AppDomain.CreateDomain("TestDomain2");

                var h4 = (IScriptedMethod)appDomain2.CreateInstanceFromAndUnwrap(assemblyPath2, "Blah.testmethod");
                object o4 = h4.Execute(new ScriptingApi(), new string[] { });

                AppDomain.Unload(appDomain2);
                File.Delete(assemblyPath2);

            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }


#if false
        
                var proxy = (ProxyDomain)appDomain.CreateInstanceAndUnwrap(
                    typeof(ProxyDomain).Assembly.FullName,
                    typeof(ProxyDomain).FullName);

                var assembly = proxy.GetAssembly(assemblyPath);

                //appDomain.Load(result.PathToAssembly);

                //var method = (IScriptedMethod)appDomain.CreateInstanceAndUnwrap(Assembly.GetEntryAssembly().FullName, "testmethod");

                //object o = method.Execute(new string[] { "hel", "lo" });


        class ProxyDomain : MarshalByRefObject
        {
            public Assembly GetAssembly(string assemblyPath)
            {
                return Assembly.LoadFile(assemblyPath);
            }
        }

#endif

        static void Benchmark(Action method, string desc)
        {
            Benchmark<int>(() => { method(); return 1; }, desc);
        }

        static T Benchmark<T>(Func<T> method, string desc)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            try
            {
                return method();
            }
            finally
            {
                sw.Stop();
                Console.WriteLine(string.Format("{0} took {1}ms", desc, sw.ElapsedMilliseconds));
            }
        }
    }
}
