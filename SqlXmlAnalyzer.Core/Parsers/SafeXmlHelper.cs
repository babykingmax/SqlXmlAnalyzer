using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace SqlXmlAnalyzer
{
    public static class SafeXmlHelper
    {
        private static readonly XmlReaderSettings Settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, // Prohibit DTD processing (prevents XXE and Entity Expansion)
            XmlResolver = null                      // Do not resolve external XML resources
        };

        public static XDocument LoadSafe(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

            using (var reader = XmlReader.Create(filePath, Settings))
            {
                return XDocument.Load(reader);
            }
        }

        public static XDocument LoadSafe(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            using (var reader = XmlReader.Create(stream, Settings))
            {
                return XDocument.Load(reader);
            }
        }

        public static XDocument LoadSafe(TextReader textReader)
        {
            if (textReader == null)
                throw new ArgumentNullException(nameof(textReader));

            using (var reader = XmlReader.Create(textReader, Settings))
            {
                return XDocument.Load(reader);
            }
        }

        public static XDocument ParseSafe(string xml)
        {
            if (string.IsNullOrEmpty(xml))
                throw new ArgumentException("XML content cannot be null or empty.", nameof(xml));

            using (var sr = new StringReader(xml))
            using (var reader = XmlReader.Create(sr, Settings))
            {
                return XDocument.Load(reader);
            }
        }
    }
}
