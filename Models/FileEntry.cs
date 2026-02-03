using System;
using System.Runtime.Serialization;

namespace FileManager.Models
{
    [DataContract]
    public class FileEntry
    {
        [DataMember] public Guid Id { get; set; } = Guid.NewGuid();
        [DataMember] public string Category { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public string Path { get; set; }
        [DataMember] public string Extension { get; set; }
        [DataMember] public long Size { get; set; }
        [DataMember] public DateTime CreatedUtc { get; set; }
        [DataMember] public string Description { get; set; }

        public bool IsTextBased()
        {
            if (string.IsNullOrEmpty(Extension)) return false;
            var ext = Extension.StartsWith(".") ? Extension.ToLowerInvariant() : "." + Extension.ToLowerInvariant();
            string[] textExt = new[] { ".md", ".markdown", ".txt", ".html", ".htm", ".xml", ".json", ".csv", ".log", ".cs", ".config", ".yml", ".yaml", ".ini", ".css", ".js" };
            return Array.IndexOf(textExt, ext) >= 0;
        }
    }
}