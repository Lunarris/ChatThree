using System.Numerics;
using System.Text;
using ChatThree.Code;
using ChatThree.Util;
using Dalamud.Game.Text.SeStringHandling;
using MessagePack;

namespace ChatThree;

[Union(0, typeof(TextChunk))]
[Union(1, typeof(IconChunk))]
[MessagePackObject]
public abstract class Chunk
{
    [IgnoreMember]
    public Message? Message { get; set; }

    [Key(0)]
    public ChunkSource Source { get; set; }

    [Key(1)]
    [MessagePackFormatter(typeof(PayloadMessagePackFormatter))]
    public Payload? Link { get; set; }

    protected Chunk(ChunkSource source, Payload? link)
    {
        Source = source;
        Link = link;
    }

    public SeString? GetSeString() => Source switch
    {
        ChunkSource.None => null,
        ChunkSource.Sender => Message?.SenderSource,
        ChunkSource.Content => Message?.ContentSource,
        _ => null,
    };

    /// <summary>
    /// Get some basic text for use in generating hashes.
    /// </summary>
    public string StringValue()
    {
        return this switch
        {
            TextChunk text => text.Content,
            IconChunk icon => icon.Icon.ToString(),
            _ => ""
        };
    }
}

public enum ChunkSource
{
    None,
    Sender,
    Content,
}

[MessagePackObject(AllowPrivate = true)]
public class TextChunk : Chunk
{
    [Key(2)] public ChatType? FallbackColor;
    [Key(3)] public uint? Foreground;
    [Key(4)] public uint? Glow;
    [Key(5)] public bool Italic;
    [Key(6)] public string Content;

    [IgnoreMember] public Vector4? Color;

    [IgnoreMember] private string? Utf8Source;
    [IgnoreMember] private byte[][] Utf8Lines = [];

    private static readonly string[] LineBreaks = ["\r\n", "\r", "\n"];

    /// <summary>
    /// UTF-8 encoded lines of Content, rebuilt only when Content is reassigned.
    /// </summary>
    public byte[][] GetUtf8Lines()
    {
        if (ReferenceEquals(Utf8Source, Content))
            return Utf8Lines;

        Utf8Source = Content;
        Utf8Lines = Content.Split(LineBreaks, StringSplitOptions.None).Select(line => Encoding.UTF8.GetBytes(line)).ToArray();
        return Utf8Lines;
    }

    private TextChunk(Chunk chunk, string content) : base(chunk.Source, chunk.Link)
    {
        Content = content;
    }

    public TextChunk(ChunkSource source, Payload? link, string content) : base(source, link)
    {
        // This has been null in the past, and it broke rendering code.
        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        Content = content ?? "";
    }

    // ReSharper disable once UnusedMember.Global // Used by MessagePack
    public TextChunk(ChunkSource source, Payload? link, ChatType? fallbackColor, uint? foreground, uint? glow, bool italic, string content) : base(source, link)
    {
        FallbackColor = fallbackColor;
        Foreground = foreground;
        Glow = glow;
        Italic = italic;
        // See above.
        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        Content = content ?? "";

        Color = ColourUtil.RgbaToVector4(foreground);
    }

    /// <summary>
    /// Creates a new TextChunk with identical styling to this one.
    /// </summary>
    public TextChunk NewWithStyle(ChunkSource source, Payload? link, string content)
    {
        return new TextChunk(source, link, content)
        {
            FallbackColor = FallbackColor,
            Foreground = Foreground,
            Glow = Glow,
            Italic = Italic,
            Color = ColourUtil.RgbaToVector4(Foreground),
        };
    }

    /// <summary>
    /// Creates a new TextChunk with identical styling to this one.
    /// </summary>
    public TextChunk NewWithStyle(Chunk chunk, string content)
    {
        return new TextChunk(chunk, content)
        {
            FallbackColor = FallbackColor,
            Foreground = Foreground,
            Glow = Glow,
            Italic = Italic,
            Color = ColourUtil.RgbaToVector4(Foreground),
        };
    }
}

[MessagePackObject(AllowPrivate = true)]
public class IconChunk : Chunk
{
    [Key(2)]
    public BitmapFontIcon Icon { get; set; }

    public IconChunk(ChunkSource source, Payload? link, BitmapFontIcon icon) : base(source, link)
    {
        Icon = icon;
    }
}
