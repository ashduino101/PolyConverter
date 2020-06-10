﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace PolyConverter
{
    public class PolyConverter
    {
        static readonly JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings {
            Formatting = Formatting.Indented,
            PreserveReferencesHandling = PreserveReferencesHandling.None,
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            Converters = new JsonConverter[] { new VectorJsonConverter(), new PolyJsonConverter() },
        };

        const string layoutExtension = ".layout";
        const string jsonExtension = ".layout.json";
        const string backupExtension = ".layout.backup";

        static readonly List<char> logChars = new List<char> { 'F', 'E', '+', '@', '*', '.', '>' };

        static readonly Regex layoutExtensionRegex = new Regex(layoutExtension.Replace(".", "\\.") + "$");
        static readonly Regex jsonExtensionRegex = new Regex(jsonExtension.Replace(".", "\\.") + "$");
        static readonly Regex backupExtensionRegex = new Regex(backupExtension.Replace(".", "\\.") + "$");

        public void Run()
        {
            var resultLog = new List<string>();
            int fileCount = 0, backups = 0;

            string[] files = null;
            try { files = Directory.GetFiles("."); }
            catch (IOException e)
            {
                Console.WriteLine($"[Fatal Error] Couldn't access files: {e.Message}.\nThe program will exit.");
                Console.ReadLine();
                Environment.Exit(1);
            }

            Console.WriteLine("[>] Working...");

            foreach (string path in files)
            {
                if (backupExtensionRegex.IsMatch(path))
                {
                    backups++;
                    continue;
                }
                else if (jsonExtensionRegex.IsMatch(path))
                {
                    string layoutPath = jsonExtensionRegex.Replace(path, layoutExtension);
                    string backupPath = jsonExtensionRegex.Replace(path, backupExtension);

                    try { resultLog.Add(JsonToLayout(path, layoutPath, backupPath)); }
                    catch (Exception e)
                    {
                        resultLog.Add($"[Fatal Error] Couldn't convert \"{PathTrim(path)}\". See below for details.\n///{e}\n///");
                        continue;
                    }

                    fileCount++;
                }
                else if (layoutExtensionRegex.IsMatch(path))
                {
                    string newPath = layoutExtensionRegex.Replace(path, jsonExtension);
                    if (File.Exists(newPath)) continue;

                    try { resultLog.Add(LayoutToJson(path, newPath)); }
                    catch (Exception e)
                    {
                        resultLog.Add($"[Fatal Error] Couldn't convert \"{PathTrim(path)}\". See below for details.\n///{e}\n///");
                        continue;
                    }
                    fileCount++;
                }
            }

            resultLog = resultLog
                .Where(s => !string.IsNullOrWhiteSpace(s) && logChars.Contains(s[1]))
                .OrderBy(s => logChars.IndexOf(s[1]))
                .ToList();

            foreach (string msg in resultLog)
                Console.WriteLine(msg);

            if (resultLog.Count == 0)
            {
                if (fileCount > 0) Console.WriteLine("[>] All files checked, no changes to apply.");
                else if (backups == 0) Console.WriteLine("[>] There are no layout files to convert in this folder.");
                else Console.WriteLine("[>] The only layouts detected are backups and were ignored.");
            }
            else Console.WriteLine($"[>] Done.");
        }

        string LayoutToJson(string layoutPath, string jsonPath)
        {
            int _ = 0;
            var bytes = File.ReadAllBytes(layoutPath);
            var dataConstructor = Program.SandboxLayoutData.GetConstructor(new Type[] { typeof(byte).MakeArrayType(), typeof(int).MakeByRefType() });
            var data = dataConstructor.Invoke(new object[] { bytes, _ });
            string json = JsonConvert.SerializeObject(data, jsonSerializerSettings);

            // Limit the indentation depth to 4 levels for compactness
            json = Regex.Replace(json, "(\r\n|\r|\n)( ){6,}", " ");
            json = Regex.Replace(json, "(\r\n|\r|\n)( ){4,}(\\}|\\])", " $3");

            try { File.WriteAllText(jsonPath, json); }
            catch (IOException e)
            {
                return $"[Error] Failed to save file \"{PathTrim(jsonPath)}\": {e.Message}";
            }

            return $"[+] Created \"{PathTrim(jsonPath)}\"";
        }

        string JsonToLayout(string jsonPath, string layoutPath, string backupPath)
        {
            string json = File.ReadAllText(jsonPath);
            object data;
            try { data = JsonConvert.DeserializeObject(json, Program.SandboxLayoutData, jsonSerializerSettings); }
            catch (JsonReaderException e)
            {
                return $"[Error] Invalid json content in \"{PathTrim(jsonPath)}\": {e.Message}";
            }

            var bytes = data.SerializeBinaryCustom();

            bool madeBackup = false;
            bool existsBefore = File.Exists(layoutPath);

            if (existsBefore)
            {
                var oldBytes = File.ReadAllBytes(layoutPath);
                if (oldBytes.SequenceEqual(bytes))
                {
                    return $"";
                }

                if (!File.Exists(backupPath))
                {
                    try { File.Copy(layoutPath, backupPath); }
                    catch (IOException e)
                    {
                        return $"[Error] Failed to create backup file \"{PathTrim(backupPath)}\": {e.Message}. Conversion aborted.";
                    }
                    madeBackup = true;
                }
            }

            try { File.WriteAllBytes(layoutPath, bytes); }
            catch (IOException e)
            {
                return $"[Error] Failed to save file \"{PathTrim(layoutPath)}\": {e.Message}";
            }

            if (existsBefore)
            {
                if (madeBackup) return $"[@] Made backup \"{PathTrim(backupPath)}\"\n[*] Applied changes to \"{PathTrim(layoutPath)}\"";
                return $"[*] Applied changes to \"{PathTrim(layoutPath)}\"";
            }
            else return $"[*] Converted json file into \"{PathTrim(layoutPath)}\"";
        }

        string PathTrim(string path)
        {
            return path.Substring(path.LastIndexOfAny(new char[] { '/', '\\' }) + 1);
        }
    }
}
