using Microsoft.Win32;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record FileDialogRequest(
        string Filter,
        string Title,
        string? DefaultExtension = null,
        string? FileName = null);

    public interface IFileDialogService
    {
        string? ShowOpenFile(FileDialogRequest request);
        string? ShowSaveFile(FileDialogRequest request);
    }

    public sealed class WpfFileDialogService : IFileDialogService
    {
        public string? ShowOpenFile(FileDialogRequest request)
        {
            var dialog = new OpenFileDialog
            {
                Filter = request.Filter,
                Title = request.Title
            };

            if (!string.IsNullOrWhiteSpace(request.DefaultExtension))
            {
                dialog.DefaultExt = request.DefaultExtension;
            }

            if (!string.IsNullOrWhiteSpace(request.FileName))
            {
                dialog.FileName = request.FileName;
            }

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public string? ShowSaveFile(FileDialogRequest request)
        {
            var dialog = new SaveFileDialog
            {
                Filter = request.Filter,
                Title = request.Title
            };

            if (!string.IsNullOrWhiteSpace(request.DefaultExtension))
            {
                dialog.DefaultExt = request.DefaultExtension;
            }

            if (!string.IsNullOrWhiteSpace(request.FileName))
            {
                dialog.FileName = request.FileName;
            }

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }
}
