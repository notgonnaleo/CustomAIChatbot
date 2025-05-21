using AIChatbot.Application.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AIChatbot.Application.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ChatbotController : ControllerBase
    {
        private readonly HttpClient _client;
        private readonly AppDbContext _context;
        private readonly ILogger<ChatbotController> _logger;
        public ChatbotController(IHttpClientFactory httpClientFactory, AppDbContext context, ILogger<ChatbotController> logger)
        {
            _client = httpClientFactory.CreateClient("Ollama");
            _context = context;
            _logger = logger;
        }

        [HttpPost]
        [Route("Embedding")]
        public async Task<IActionResult> Add([FromBody] string message)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "api/embeddings");
                var payload = new OllamaRequest()
                {
                    Model = "nomic-embed-text",
                    Prompt = message
                };

                request.Content = new StringContent(JsonSerializer.Serialize(payload));
                var response = await _client.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var embeddingVectorResponse = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>();
                if (embeddingVectorResponse == null || embeddingVectorResponse.Embedding == null)
                {
                    throw new Exception("Embedding vector not generated or returned null.");
                }

                var embedding = new TextEmbedding
                {
                    Message = message,
                    Embedding = FloatArrayToByteArray(embeddingVectorResponse.Embedding)
                };
                await _context.AddAsync(embedding);
                await _context.SaveChangesAsync();

                return Ok(embedding);
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message, StackTrace = ex.StackTrace });
            }
        }

        [HttpPost("Ask")]
        public async Task<IActionResult> Ask([FromBody] string question)
        {
            try
            {
                // Create embedding for the question
                var request = new HttpRequestMessage(HttpMethod.Post, "api/embeddings");
                request.Content = new StringContent(JsonSerializer.Serialize(new OllamaRequest() 
                { 
                    Model = "nomic-embed-text",
                    Prompt = question 
                }), System.Text.Encoding.UTF8, "application/json");
                var response = await _client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                var embeddingResponse = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>();

                if (embeddingResponse == null || embeddingResponse.Embedding == null)
                    throw new Exception("Embedding generation failed.");

                var questionEmbedding = embeddingResponse.Embedding;

                // Find best matches in the database by embedding score
                // TODO: Improve this matching algorithm, or add more data probably?
                var allEmbeddings = await _context.Embeddings.ToListAsync();
                var topMatches = allEmbeddings
                    .Select(e => new
                    {
                        TextEmbedding = e,
                        Score = CosineSimilarity(questionEmbedding, ByteArrayToFloatArray(e.Embedding)) // Fix this stupid warning
                    })
                    .OrderByDescending(x => x.Score)
                    .ToList();
                _logger.LogTrace($"Closest embeddings to question: {topMatches}");

                if (topMatches.Count == 0)
                    return NotFound("I don't have enough context to answer that, Could you try something different?");

                // Build pre-context for question
                var context = string.Join("\n---\n", topMatches.Select(x => x.TextEmbedding.Message));

                // Create prompt
                var prompt = 
                @$"
                    You're an expert assistant with a senior software engineering level and problem solver, doing whatever workaround necessary to accomplish your goals.
                    
                    If possible use all you can from the 'Context' topic as a base for your answer, and if necessary, Use online resources
                    to complete the answer;

                    You must try as hard as you can to answer the question, but if you don't know the answer then say that you don't know and
                    if possible, suggest a way to find the answer;

                    Answer using the same language which question was written with;
                    
                    Context:
                    {context}

                    Question:
                    {question}
                ";

                // Send to AI Agent
                var chatRequest = new OllamaRequest
                {
                    Model = "llama3",
                    Prompt = prompt
                };
                _logger.LogTrace("Sending request to AI agent...");
                _logger.LogTrace(JsonSerializer.Serialize(chatRequest));

                var chatHttpRequest = new HttpRequestMessage(HttpMethod.Post, "api/generate")
                {
                    Content = new StringContent(JsonSerializer.Serialize(chatRequest), System.Text.Encoding.UTF8, "application/json")
                };
                var chatResponse = await _client.SendAsync(chatHttpRequest);
                chatResponse.EnsureSuccessStatusCode();

                using var stream = await chatResponse.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);

                var fragments = new List<OllamaChatResponse>();
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        var fragment = JsonSerializer.Deserialize<OllamaChatResponse>(line);
                        if (fragment != null)
                            fragments.Add(fragment);
                    }
                }

                var fullResponse = string.Join("", fragments.Select(f => f.Response));
                var finalFragment = fragments.LastOrDefault(f => f.Done);

                return Ok(new
                {
                    Answer = fullResponse,
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }
        private static float CosineSimilarity(float[] a, float[] b)
        {
            float dot = 0f, magA = 0f, magB = 0f;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                magA += a[i] * a[i];
                magB += b[i] * b[i];
            }
            return dot / (MathF.Sqrt(magA) * MathF.Sqrt(magB));
        }
        private static float[] ByteArrayToFloatArray(byte[] bytes)
        {
            float[] floats = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
            return floats;
        }

        private static byte[] FloatArrayToByteArray(float[] array)
        {
            var result = new byte[array.Length * sizeof(float)];
            Buffer.BlockCopy(array, 0, result, 0, result.Length);
            return result;
        }
    }
}
