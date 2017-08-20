using log4net;

namespace Resin.Documents
{
    public class DocumentWriteCommand : IDocumentWriteCommand
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(DocumentWriteCommand));

        public void Write(Document document, IWriteSession session)
        {
            session.Write(document);

            Log.DebugFormat("stored doc ID {0}", document.Id);
        }
    }
}