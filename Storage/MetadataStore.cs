using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using FileManager.Models;

namespace FileManager.Storage
{
    [DataContract]
    public class MetadataContainer
    {
        [DataMember] public List<string> Categories { get; set; } = new List<string>();
        [DataMember] public List<FileEntry> Entries { get; set; } = new List<FileEntry>();
    }

    public class MetadataStore
    {
        private readonly string _mdPath;

        public MetadataStore()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileManager");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            _mdPath = Path.Combine(dir, "metadata.md");
        }

        // 返回包含 Categories 与 Entries 的容器（不自动注入“未分类”）
        public MetadataContainer Load()
        {
            if (!File.Exists(_mdPath)) return new MetadataContainer();

            var text = File.ReadAllText(_mdPath, Encoding.UTF8);
            var start = text.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
            if (start < 0) return new MetadataContainer();
            start = text.IndexOf('\n', start);
            if (start < 0) return new MetadataContainer();
            var end = text.IndexOf("```", start + 1);
            if (end < 0) return new MetadataContainer();
            var json = text.Substring(start + 1, end - start - 1).Trim();

            if (string.IsNullOrWhiteSpace(json)) return new MetadataContainer();

            try
            {
                var trimmed = json.TrimStart();
                // 兼容老格式：旧版直接保存 List<FileEntry>
                if (trimmed.StartsWith("["))
                {
                    var serOld = new DataContractJsonSerializer(typeof(List<FileEntry>));
                    using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    {
                        var list = serOld.ReadObject(ms) as List<FileEntry> ?? new List<FileEntry>();
                        var container = new MetadataContainer
                        {
                            Entries = list,
                            // 只收集非空的分类，不自动加入“未分类”
                            Categories = list.Select(e => e.Category).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList()
                        };
                        return container;
                    }
                }
                else
                {
                    var ser = new DataContractJsonSerializer(typeof(MetadataContainer));
                    using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    {
                        var container = ser.ReadObject(ms) as MetadataContainer;
                        if (container == null) return new MetadataContainer();
                        // 不要自动加入“未分类”
                        return container;
                    }
                }
            }
            catch
            {
                return new MetadataContainer();
            }
        }

        // 保存容器（categories + entries）
        public void Save(MetadataContainer container)
        {
            if (container == null) container = new MetadataContainer();
            var ser = new DataContractJsonSerializer(typeof(MetadataContainer));
            using (var ms = new MemoryStream())
            {
                ser.WriteObject(ms, container);
                var json = Encoding.UTF8.GetString(ms.ToArray());
                var md = new StringBuilder();
                md.AppendLine("# FileManager metadata");
                md.AppendLine();
                md.AppendLine("下面的 JSON 区块为程序使用的元数据，请不要手动破坏结构。");
                md.AppendLine();
                md.AppendLine("```json");
                md.AppendLine(json);
                md.AppendLine("```");
                File.WriteAllText(_mdPath, md.ToString(), Encoding.UTF8);
            }
        }

        public string MdPath => _mdPath;
    }
}