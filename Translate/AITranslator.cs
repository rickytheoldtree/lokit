using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using LocalizationTranslationWPF.Data;
using Newtonsoft.Json.Linq;

namespace LocalizationTranslationWPF.Translate
{
    public class AITranslator
    {
        private const string Prompt = "请将 {0} 翻译成 {1}，只回答我json格式，不要回答额外信息，也不用加json的markdown";

        // Replace with your actual OpenAI API key
        private readonly string apiKey;
        private readonly string endpoint;
        private readonly string modelName;
        private readonly string prompt;
        public AITranslator(string apiKey, string endpoint, string modelName, string prompt)
        {
            this.apiKey = apiKey;
            this.endpoint = endpoint;
            this.modelName = modelName;
            this.prompt = prompt;
        }
        public async Task<string> Translate(string text, Pair to)
        {
            // Format the prompt with the input text and target language (to.Name)
            var contentText = string.Format(Prompt, text, to.Name);

            // Prepare the request payload
            var requestBody = new
            {
                model = modelName,
                messages = new[]
                {
                    new { role = "system", content = prompt },
                    new { role = "user", content = contentText }
                }
            };

            var jsonRequestBody = JsonConvert.SerializeObject(requestBody);

            using (var client = new HttpClient())
            {
                // Set the authorization header with your API key
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                var httpContent = new StringContent(jsonRequestBody, Encoding.UTF8, "application/json");

                // Send POST request to the ChatGPT API endpoint
                var response = await client.PostAsync(endpoint, httpContent);

                if (response.IsSuccessStatusCode)
                {
                    // Read the response string (which should be in JSON format as requested)
                    var responseContent = await response.Content.ReadAsStringAsync();

                    // Optionally, parse the JSON response to extract the translation.
                    // For example, you might use JObject from Newtonsoft.Json:
                    var jsonResponse = JObject.Parse(responseContent);
                    var translatedText = jsonResponse["choices"]?[0]?["message"]?["content"]?.ToString();

                    return translatedText;
                }
                else
                {
                    // Handle error response
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"API request failed: {response.StatusCode}, {errorContent}");
                }
            }
        }
    }
}
