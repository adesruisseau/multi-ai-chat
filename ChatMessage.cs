using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace AgentGroupChat;

public sealed class ChatMessage : INotifyPropertyChanged
{
    private string _content;
    private Brush _accentBrush;
    private Brush _cardBackground;
    private string _speechPrefix;
    private string _speechCurrent;
    private string _speechSuffix;
    private string _activeSpeechSegment;
    private int _speechSearchStartIndex;

    public ChatMessage(string author, string content, Brush accentBrush, Brush cardBackground, string? timestampText = null)
    {
        Author = author;
        _content = content;
        _accentBrush = accentBrush;
        _cardBackground = cardBackground;
        _speechPrefix = content;
        _speechCurrent = string.Empty;
        _speechSuffix = string.Empty;
        _activeSpeechSegment = string.Empty;
        TimestampText = string.IsNullOrWhiteSpace(timestampText)
            ? DateTime.Now.ToString("h:mm tt")
            : timestampText;
    }

    public string Author { get; }

    public string Content
    {
        get => _content;
        set
        {
            if (_content == value)
            {
                return;
            }

            _content = value;
            OnPropertyChanged();
            RefreshSpeechDisplay();
        }
    }

    public string TimestampText { get; }

    public string SpeechPrefix => _speechPrefix;

    public string SpeechCurrent => _speechCurrent;

    public string SpeechSuffix => _speechSuffix;

    public bool HasSpeechHighlight => !string.IsNullOrEmpty(_speechCurrent);

    public Brush AccentBrush
    {
        get => _accentBrush;
        set
        {
            if (ReferenceEquals(_accentBrush, value))
            {
                return;
            }

            _accentBrush = value;
            OnPropertyChanged();
        }
    }

    public Brush CardBackground
    {
        get => _cardBackground;
        set
        {
            if (ReferenceEquals(_cardBackground, value))
            {
                return;
            }

            _cardBackground = value;
            OnPropertyChanged();
        }
    }

    public void SetSpeechHighlight(string segmentText)
    {
        _activeSpeechSegment = string.IsNullOrWhiteSpace(segmentText)
            ? string.Empty
            : segmentText.Trim();
        RefreshSpeechDisplay();
    }

    public void ClearSpeechHighlight()
    {
        _activeSpeechSegment = string.Empty;
        _speechSearchStartIndex = 0;
        UpdateSpeechDisplay(_content, string.Empty, string.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RefreshSpeechDisplay()
    {
        if (string.IsNullOrWhiteSpace(_activeSpeechSegment))
        {
            UpdateSpeechDisplay(_content, string.Empty, string.Empty);
            return;
        }

        var segment = _activeSpeechSegment;
        var searchStart = Math.Clamp(_speechSearchStartIndex, 0, _content.Length);
        var segmentStartIndex = _content.IndexOf(segment, searchStart, StringComparison.Ordinal);
        if (segmentStartIndex < 0)
        {
            segmentStartIndex = _content.IndexOf(segment, StringComparison.Ordinal);
        }

        if (segmentStartIndex < 0)
        {
            UpdateSpeechDisplay(_content, string.Empty, string.Empty);
            return;
        }

        _speechSearchStartIndex = segmentStartIndex + segment.Length;
        UpdateSpeechDisplay(
            _content[..segmentStartIndex],
            _content.Substring(segmentStartIndex, segment.Length),
            _content[(segmentStartIndex + segment.Length)..]);
    }

    private void UpdateSpeechDisplay(string prefix, string current, string suffix)
    {
        var hadHighlight = HasSpeechHighlight;
        var hasHighlight = !string.IsNullOrEmpty(current);

        if (_speechPrefix != prefix)
        {
            _speechPrefix = prefix;
            OnPropertyChanged(nameof(SpeechPrefix));
        }

        if (_speechCurrent != current)
        {
            _speechCurrent = current;
            OnPropertyChanged(nameof(SpeechCurrent));
        }

        if (_speechSuffix != suffix)
        {
            _speechSuffix = suffix;
            OnPropertyChanged(nameof(SpeechSuffix));
        }

        if (hadHighlight != hasHighlight)
        {
            OnPropertyChanged(nameof(HasSpeechHighlight));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}