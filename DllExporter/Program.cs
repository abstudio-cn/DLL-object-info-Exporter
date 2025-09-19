using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;
using Newtonsoft.Json;

namespace DllExporter
{
    // 原有的数据模型类保持不变
    public class DllTypeInfo
    {
        public string Name { get; set; }
        public string Namespace { get; set; }
        public string FullName { get; set; }
        public Type Type { get; set; }
        public bool IsClass { get; set; }
        public bool IsInterface { get; set; }
        public bool IsEnum { get; set; }
        public bool IsValueType { get; set; }
        public List<DllMethodInfo> Methods { get; set; }
        public List<DllPropertyInfo> Properties { get; set; }
        public List<DllFieldInfo> Fields { get; set; }
    }

    public class DllMethodInfo
    {
        public string Name { get; set; }
        public string ReturnType { get; set; }
        public List<DllParameterInfo> Parameters { get; set; }
        public bool IsPublic { get; set; }
    }

    public class DllPropertyInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public bool CanRead { get; set; }
        public bool CanWrite { get; set; }
    }

    public class DllFieldInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public bool IsPublic { get; set; }
    }

    public class DllParameterInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
    }

    public class DllExporter
    {
        private Assembly _assembly;
        private string _dllDirectory;
        private List<string> _resolvedDependencies = new List<string>();
        private List<string> _ignoredDependencies = new List<string>();

        // 系统程序集和资源程序集列表，这些不需要手动解析
        private readonly HashSet<string> _systemAssemblies = new HashSet<string>
        {
            "mscorlib", "System", "System.Core", "System.Xml", "System.Data",
            "System.Configuration", "System.Runtime", "System.Private.CoreLib",
            "netstandard", "Microsoft.Win32.Primitives", "System.AppContext",
            "System.Collections", "System.Collections.Concurrent", "System.Console",
            "System.Diagnostics.Debug", "System.Diagnostics.Tools", "System.Diagnostics.Tracing",
            "System.Globalization", "System.IO", "System.IO.Compression", "System.IO.FileSystem",
            "System.IO.FileSystem.Primitives", "System.Linq", "System.Linq.Expressions",
            "System.Net.Primitives", "System.Net.Sockets", "System.ObjectModel",
            "System.Reflection", "System.Reflection.Extensions", "System.Reflection.Primitives",
            "System.Resources.ResourceManager", "System.Runtime.Extensions",
            "System.Runtime.Handles", "System.Runtime.InteropServices",
            "System.Runtime.InteropServices.RuntimeInformation", "System.Runtime.Numerics",
            "System.Security.Cryptography.Algorithms", "System.Security.Cryptography.Primitives",
            "System.Text.Encoding", "System.Text.Encoding.Extensions", "System.Text.RegularExpressions",
            "System.Threading", "System.Threading.Tasks", "System.Threading.Thread",
            "System.Threading.Timer", "System.ValueTuple", "System.Xml.ReaderWriter",
            "System.Xml.XmlDocument", "System.Xml.XPath", "System.Xml.XPath.XDocument"
        };

        public void LoadAssembly(string dllPath)
        {
            if (!File.Exists(dllPath))
                throw new FileNotFoundException("DLL文件未找到", dllPath);

            _dllDirectory = Path.GetDirectoryName(dllPath);

            // 设置程序集解析事件处理程序
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;

            try
            {
                _assembly = Assembly.LoadFrom(dllPath);
                Console.WriteLine($"成功加载程序集: {_assembly.FullName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载程序集时出错: {ex.Message}");
                throw;
            }
        }

        // 程序集解析事件处理程序
        private Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            // 获取程序集名称
            var assemblyName = new AssemblyName(args.Name).Name;

            // 检查是否是系统程序集
            if (IsSystemAssembly(assemblyName))
            {
                if (!_ignoredDependencies.Contains(assemblyName))
                {
                    Console.WriteLine($"忽略系统程序集: {assemblyName}");
                    _ignoredDependencies.Add(assemblyName);
                }
                return null;
            }

            // 检查是否是资源程序集
            if (assemblyName.EndsWith(".resources"))
            {
                if (!_ignoredDependencies.Contains(assemblyName))
                {
                    Console.WriteLine($"忽略资源程序集: {assemblyName}");
                    _ignoredDependencies.Add(assemblyName);
                }
                return null;
            }

            // 检查是否是XML序列化程序集
            if (assemblyName.EndsWith(".XmlSerializers"))
            {
                if (!_ignoredDependencies.Contains(assemblyName))
                {
                    Console.WriteLine($"忽略XML序列化程序集: {assemblyName}");
                    _ignoredDependencies.Add(assemblyName);
                }
                return null;
            }

            // 避免重复解析
            if (_resolvedDependencies.Contains(assemblyName))
                return null;

            Console.WriteLine($"尝试解析依赖: {assemblyName}");

            // 尝试在DLL同目录下查找
            var assemblyPath = Path.Combine(_dllDirectory, assemblyName + ".dll");
            if (File.Exists(assemblyPath))
            {
                try
                {
                    var resolvedAssembly = Assembly.LoadFrom(assemblyPath);
                    _resolvedDependencies.Add(assemblyName);
                    Console.WriteLine($"成功解析依赖: {assemblyName}");
                    return resolvedAssembly;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"加载依赖 {assemblyName} 时出错: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"依赖 {assemblyName} 未在目录 {_dllDirectory} 中找到");
            }

            return null;
        }

        private bool IsSystemAssembly(string assemblyName)
        {
            return _systemAssemblies.Contains(assemblyName) ||
                   assemblyName.StartsWith("System.") ||
                   assemblyName.StartsWith("Microsoft.") ||
                   assemblyName.StartsWith("netstandard") ||
                   assemblyName.StartsWith("mscorlib");
        }

        public List<DllTypeInfo> GetTypesInfo()
        {
            if (_assembly == null)
                throw new InvalidOperationException("未加载任何程序集");

            var typesInfo = new List<DllTypeInfo>();

            try
            {
                foreach (var type in _assembly.GetTypes())
                {
                    // 跳过编译器生成的类型和无法访问的类型
                    if (type.IsSpecialName || !type.IsPublic) continue;

                    try
                    {
                        var typeInfo = new DllTypeInfo
                        {
                            Name = type.Name,
                            Namespace = type.Namespace,
                            FullName = type.FullName,
                            Type = type,
                            IsClass = type.IsClass,
                            IsInterface = type.IsInterface,
                            IsEnum = type.IsEnum,
                            IsValueType = type.IsValueType,
                            Methods = GetMethodsInfo(type),
                            Properties = GetPropertiesInfo(type),
                            Fields = GetFieldsInfo(type)
                        };

                        typesInfo.Add(typeInfo);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"处理类型 {type.Name} 时出错: {ex.Message}");
                        // 继续处理其他类型
                    }
                }
            }
            catch (ReflectionTypeLoadException ex)
            {
                Console.WriteLine($"加载类型时出错: {ex.Message}");
                foreach (var loaderException in ex.LoaderExceptions)
                {
                    Console.WriteLine($"加载异常: {loaderException.Message}");
                }

                // 尝试处理已加载的类型
                foreach (var type in ex.Types.Where(t => t != null && t.IsPublic && !t.IsSpecialName))
                {
                    try
                    {
                        var typeInfo = new DllTypeInfo
                        {
                            Name = type.Name,
                            Namespace = type.Namespace,
                            FullName = type.FullName,
                            Type = type,
                            IsClass = type.IsClass,
                            IsInterface = type.IsInterface,
                            IsEnum = type.IsEnum,
                            IsValueType = type.IsValueType,
                            Methods = GetMethodsInfo(type),
                            Properties = GetPropertiesInfo(type),
                            Fields = GetFieldsInfo(type)
                        };

                        typesInfo.Add(typeInfo);
                    }
                    catch (Exception typeEx)
                    {
                        Console.WriteLine($"处理类型 {type.Name} 时出错: {typeEx.Message}");
                    }
                }
            }

            return typesInfo;
        }

        // 其他方法保持不变...
        private List<DllMethodInfo> GetMethodsInfo(Type type)
        {
            return type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .Select(m => new DllMethodInfo
                {
                    Name = m.Name,
                    ReturnType = m.ReturnType.Name,
                    Parameters = m.GetParameters().Select(p => new DllParameterInfo
                    {
                        Name = p.Name,
                        Type = p.ParameterType.Name
                    }).ToList(),
                    IsPublic = m.IsPublic
                }).ToList();
        }

        private List<DllPropertyInfo> GetPropertiesInfo(Type type)
        {
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(p => new DllPropertyInfo
                {
                    Name = p.Name,
                    Type = p.PropertyType.Name,
                    CanRead = p.CanRead,
                    CanWrite = p.CanWrite
                }).ToList();
        }

        private List<DllFieldInfo> GetFieldsInfo(Type type)
        {
            return type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(f => !f.IsSpecialName)
                .Select(f => new DllFieldInfo
                {
                    Name = f.Name,
                    Type = f.FieldType.Name,
                    IsPublic = f.IsPublic
                }).ToList();
        }

        public string ExportAsText(List<DllTypeInfo> typesInfo)
        {
            var sb = new StringBuilder();

            // 添加解析摘要
            sb.AppendLine("=== 依赖解析摘要 ===");
            sb.AppendLine($"成功解析的依赖: {string.Join(", ", _resolvedDependencies)}");
            sb.AppendLine($"忽略的系统/资源依赖: {string.Join(", ", _ignoredDependencies)}");
            sb.AppendLine("===================");
            sb.AppendLine();

            foreach (var typeInfo in typesInfo)
            {
                sb.AppendLine($"类型: {typeInfo.FullName}");
                sb.AppendLine($"种类: {(typeInfo.IsClass ? "类" : typeInfo.IsInterface ? "接口" : typeInfo.IsEnum ? "枚举" : "值类型")}");
                sb.AppendLine();

                if (typeInfo.Properties.Any())
                {
                    sb.AppendLine("属性:");
                    foreach (var prop in typeInfo.Properties)
                    {
                        sb.AppendLine($"  {prop.Type} {prop.Name} {{ {(prop.CanRead ? "get;" : "")} {(prop.CanWrite ? "set;" : "")} }}");
                    }
                    sb.AppendLine();
                }

                if (typeInfo.Fields.Any())
                {
                    sb.AppendLine("字段:");
                    foreach (var field in typeInfo.Fields)
                    {
                        sb.AppendLine($"  {(field.IsPublic ? "public" : "non-public")} {field.Type} {field.Name}");
                    }
                    sb.AppendLine();
                }

                if (typeInfo.Methods.Any())
                {
                    sb.AppendLine("方法:");
                    foreach (var method in typeInfo.Methods)
                    {
                        var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Type} {p.Name}"));
                        sb.AppendLine($"  {(method.IsPublic ? "public" : "non-public")} {method.ReturnType} {method.Name}({parameters})");
                    }
                    sb.AppendLine();
                }

                sb.AppendLine(new string('-', 80));
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public string ExportAsJson(List<DllTypeInfo> typesInfo)
        {
            // 在JSON输出中也包含解析摘要
            var result = new
            {
                ResolvedDependencies = _resolvedDependencies,
                IgnoredDependencies = _ignoredDependencies,
                Types = typesInfo
            };

            return JsonConvert.SerializeObject(result, Formatting.Indented);
        }

        public string ExportAsXml(List<DllTypeInfo> typesInfo)
        {
            // 创建一个包含所有信息的对象
            var exportData = new
            {
                ResolvedDependencies = _resolvedDependencies.ToArray(),
                IgnoredDependencies = _ignoredDependencies.ToArray(),
                Types = typesInfo
            };

            // 使用Json.NET转换为XML（因为直接序列化匿名对象到XML比较复杂）
            var json = JsonConvert.SerializeObject(exportData);
            var xmlDoc = JsonConvert.DeserializeXmlNode("{\"ExportData\":" + json + "}", "Root");
            return xmlDoc.OuterXml;
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("用法: DllExporter <dll路径> [输出格式: text|json|xml] [输出文件路径]");
                Console.WriteLine("示例: DllExporter MyLibrary.dll json output.json");
                Console.WriteLine("示例: DllExporter MyLibrary.dll text");
                Console.WriteLine("注意: 确保所有非系统依赖的DLL都在目标DLL的同目录下");
                return;
            }

            string dllPath = args[0];
            string format = args.Length > 1 ? args[1].ToLower() : "text";
            string outputPath = args.Length > 2 ? args[2] : null;

            try
            {
                var exporter = new DllExporter();
                exporter.LoadAssembly(dllPath);
                var typesInfo = exporter.GetTypesInfo();

                string result;
                switch (format)
                {
                    case "json":
                        result = exporter.ExportAsJson(typesInfo);
                        break;
                    case "xml":
                        result = exporter.ExportAsXml(typesInfo);
                        break;
                    default:
                        result = exporter.ExportAsText(typesInfo);
                        break;
                }

                if (!string.IsNullOrEmpty(outputPath))
                {
                    File.WriteAllText(outputPath, result);
                    Console.WriteLine($"结果已导出到: {outputPath}");
                }
                else
                {
                    Console.WriteLine(result);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"错误: {ex.Message}");
                Console.WriteLine("请确保:");
                Console.WriteLine("1. DLL路径正确");
                Console.WriteLine("2. 所有非系统依赖的DLL都在同一目录下");
                Console.WriteLine("3. 系统依赖项（如System.Private.CoreLib）会自动忽略");
            }
            finally
            {
                Console.WriteLine("处理完成。系统依赖项已自动忽略，无需手动提供。");
            }
        }
    }
}