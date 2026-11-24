using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace Inspector;

class EnumDump
{
    public static void DumpEnums()
    {
        string pkg = @"C:\Program Files\WindowsApps\KOREINDUSTRIES.NotchPixel_0.5.113.0_x64__qgfctd1hmk5vm";
        string[] targets = new[] { "NotchPixel.Core.dll", "NotchPixel.dll" };

        var allEnums = new Dictionary<string, List<string>>();

        foreach (var target in targets)
        {
            string path = Path.Combine(pkg, target);
            if (!File.Exists(path)) continue;

            using var fs = File.OpenRead(path);
            using var pe = new PEReader(fs);
            var md = pe.GetMetadataReader();

            foreach (var th in md.TypeDefinitions)
            {
                var td = md.GetTypeDefinition(th);
                string ns = md.GetString(td.Namespace);
                string name = md.GetString(td.Name);

                if (string.IsNullOrEmpty(ns) || ns.Length <= 2 || ns.StartsWith("<")) continue;

                // Check if base type is Enum
                if (!td.BaseType.IsNil)
                {
                    var fields = new List<string>();
                    foreach (var fh in td.GetFields())
                    {
                        var fd = md.GetFieldDefinition(fh);
                        string fName = md.GetString(fd.Name);
                        if (fName != "value__")
                        {
                            fields.Add(fName);
                        }
                    }
                    if (fields.Count > 0)
                    {
                        allEnums[$"{ns}.{name}"] = fields;
                    }
                }
            }
        }

        string json = JsonSerializer.Serialize(allEnums, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(@"P:\Reverse engineering applications\X-Notch\research\enums_dump.json", json);
        Console.WriteLine($"Saved {allEnums.Count} domain enums to enums_dump.json");
    }
}
