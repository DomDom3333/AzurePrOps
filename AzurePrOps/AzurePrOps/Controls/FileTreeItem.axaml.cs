using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AzurePrOps.ReviewLogic.Models;

namespace AzurePrOps.Controls
{
    public partial class FileTreeItem : UserControl
    {
        public static readonly StyledProperty<FileDiff?> FileDiffProperty =
            AvaloniaProperty.Register<FileTreeItem, FileDiff?>(nameof(FileDiff));

        public static readonly StyledProperty<bool> IsSelectedProperty =
            AvaloniaProperty.Register<FileTreeItem, bool>(nameof(IsSelected));

        private Border? _container;
        private TextBlock? _fileIcon;
        private TextBlock? _fileName;
        private TextBlock? _filePath;
        private Border? _addedLines;
        private Border? _removedLines;
        private TextBlock? _addedText;
        private TextBlock? _removedText;
        private TextBlock? _statusIcon;

        public FileDiff? FileDiff
        {
            get => GetValue(FileDiffProperty);
            set => SetValue(FileDiffProperty, value);
        }

        public bool IsSelected
        {
            get => GetValue(IsSelectedProperty);
            set => SetValue(IsSelectedProperty, value);
        }

        static FileTreeItem()
        {
            FileDiffProperty.Changed.AddClassHandler<FileTreeItem>((x, e) => x.UpdateDisplay());
            IsSelectedProperty.Changed.AddClassHandler<FileTreeItem>((x, e) => x.UpdateSelection());
        }

        public FileTreeItem()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            
            _container = this.FindControl<Border>("PART_Container");
            _fileIcon = this.FindControl<TextBlock>("PART_FileIcon");
            _fileName = this.FindControl<TextBlock>("PART_FileName");
            _filePath = this.FindControl<TextBlock>("PART_FilePath");
            _addedLines = this.FindControl<Border>("PART_AddedLines");
            _removedLines = this.FindControl<Border>("PART_RemovedLines");
            _addedText = this.FindControl<TextBlock>("PART_AddedText");
            _removedText = this.FindControl<TextBlock>("PART_RemovedText");
            _statusIcon = this.FindControl<TextBlock>("PART_StatusIcon");

            UpdateDisplay();
            UpdateSelection();
        }

        private void UpdateDisplay()
        {
            if (FileDiff == null) return;

            // Update file info
            var fileName = Path.GetFileName(FileDiff.FilePath);
            var directory = Path.GetDirectoryName(FileDiff.FilePath);

            if (_fileName != null)
                _fileName.Text = fileName;
            if (_filePath != null)
                _filePath.Text = directory;

            // Update file icon based on extension
            if (_fileIcon != null)
                _fileIcon.Text = GetFileIcon(FileDiff.FilePath);

            // Update change statistics (simplified for demo)
            var oldLines = FileDiff.OldText?.Split('\n').Length ?? 0;
            var newLines = FileDiff.NewText?.Split('\n').Length ?? 0;
            var addedLines = Math.Max(0, newLines - oldLines);
            var removedLines = Math.Max(0, oldLines - newLines);

            if (_addedLines != null && _addedText != null)
            {
                if (addedLines > 0)
                {
                    _addedLines.IsVisible = true;
                    _addedText.Text = $"+{addedLines}";
                }
                else
                {
                    _addedLines.IsVisible = false;
                }
            }

            if (_removedLines != null && _removedText != null)
            {
                if (removedLines > 0)
                {
                    _removedLines.IsVisible = true;
                    _removedText.Text = $"-{removedLines}";
                }
                else
                {
                    _removedLines.IsVisible = false;
                }
            }

            // Update status icon
            if (_statusIcon != null)
                _statusIcon.Text = GetStatusIcon(addedLines, removedLines);
        }

        private void UpdateSelection()
        {
            if (_container == null) return;

            if (IsSelected)
            {
                _container.Classes.Add("selected");
            }
            else
            {
                _container.Classes.Remove("selected");
            }
        }

        private static string GetFileIcon(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension switch
            {
                ".cs" => "📄",
                ".js" or ".ts" => "📜",
                ".html" or ".htm" => "🌐",
                ".css" or ".scss" or ".sass" => "🎨",
                ".json" => "📋",
                ".xml" => "📰",
                ".md" => "📝",
                ".sql" => "🗃️",
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" => "🖼️",
                ".txt" => "📄",
                _ => "📄"
            };
        }

        private static string GetStatusIcon(int added, int removed)
        {
            if (added > 0 && removed > 0) return "🔄"; // Modified
            if (added > 0) return "➕"; // Added
            if (removed > 0) return "➖"; // Removed
            return "📄"; // Unchanged
        }
    }
}
