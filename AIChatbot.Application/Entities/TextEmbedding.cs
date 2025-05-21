namespace AIChatbot.Application.Entities
{
    public class TextEmbedding
    {
        public int Id { get; set; }
        public string? Message { get; set; }
        public byte[]? Embedding { get; set; }
    }
}
