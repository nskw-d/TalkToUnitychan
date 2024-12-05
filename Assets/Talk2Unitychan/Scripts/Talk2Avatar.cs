using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using System.Threading.Tasks;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

public class Talk2Avatar : MonoBehaviour
{
    // Request/Response Class Definition
    // Chat Completion Request
    [Serializable]
    class Message  // class of ChatCompletionsRequest component
    {
        public string role;
        public string content;

        public Message(string role, string content)
        {
            this.role = role;
            this.content = content;
        }
    }

    [Serializable]
    class ChatCompletionsRequest  // class to convert json for ChatComplationRequest
    {
        public string model;
        public List<Message> messages;

        public ChatCompletionsRequest(string model)
        {
            this.model = model;
            this.messages = new List<Message>();
        }
    }

    // Chat Completion Response
    [Serializable]
    class Choice  // class of ChatCompletionResponse component
    {
        public string index;
        public Message message;
        public string finish_reason;
    }

    [Serializable]
    class Usage  // class of ChatCompletionResponse component
    {
        public int prompt_tokens;
        public int completion_tokens;
        public int total_tokens;
        public string system_fingerprint;
    }

    [Serializable]
    class ChatCompletionResponse  // class to convert json for ChatComplationResponse
    {
        public string id;
        public string @object;
        public int created;
        public string model;
        public string system_fingerprint;
        public List<Choice> choices;
        public Usage usage;
        public string systyem_fingerprint;
    }

    // Create Speech Request
    [Serializable]
    class CreateSpeechRequest  // Class to convert json for CreateSpeechRequest
    {
        public string model;
        public string input;
        public string voice;
        public string response_format;

        public CreateSpeechRequest(string voice, string input)
        {
            model = "tts-1-hd";
            this.input = input;
            this.voice = voice;
            response_format = "pcm";
        }
    }

    enum Role
    {
        assistant,
        user,
    }

    // Open AI Settings
    string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");  // Set Your API Key
    string completionsURL = "https://api.openai.com/v1/chat/completions";
    string createSpeechURL = "https://api.openai.com/v1/audio/speech";
    ChatCompletionsRequest completionRequest = new ChatCompletionsRequest("gpt-4o");
    [SerializeField]
    string voice = "nova";
    [SerializeField, TextArea]
    string systemPrompt;

    // Settings Inspector Parameter
    [SerializeField]
    AudioSource audioSource;  // AudioClip connections

    [SerializeField]
    InputField inputField;  // User input field

    [SerializeField]
    int chatSpacing = 120;  // spacing between chat panel

    [SerializeField]
    GameObject chatPanelPrefab;  // use this prefab to display chat history

    [SerializeField]
    GameObject scrollViewContent;  // scroll view object for chat history

    // Controll PCM Audio Data
    int sampleRate = 24000;  // Reference: https://platform.openai.com/docs/guides/text-to-speech#voice-options
    float[] audioData = new float[0];  // received audio data
    int audioPosition = 0;  // controll playing audio position
    static readonly object audioLock = new object();  // thread safe object for audio data

    

    private void Awake()
    {
        audioSource.clip = AudioClip.Create("Text2Speech", sampleRate * 2, 1, sampleRate, true, GetPcmAudio);
        if (systemPrompt != String.Empty) 
        {
            completionRequest.messages.Add(new Message("system", systemPrompt));
        }
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        audioSource.loop = true;
        audioSource.Play();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void CreateChatPanel(string text, Role role, int createIndex) 
    {
        string role_str = "";
        switch (role) 
        {
            case Role.assistant:
                role_str = "<color=green>Unity Chan</color>\n";
                break;
            case Role.user:
                role_str = "<color=blue>User</color>\n";
                break;
        }

        var obj = Instantiate(chatPanelPrefab, scrollViewContent.transform);
        obj.GetComponentInChildren<Text>().text = role_str + text;
    }

    public void OnClickSendMessageButton()
    {
        if(inputField.text == String.Empty) 
        {
            Debug.Log("Input field is empty!");
            return;
        }
        completionRequest.messages.Add(new Message("user", inputField.text));
        CreateChatPanel(inputField.text, Role.user, completionRequest.messages.Count - 1);
        StartCoroutine(SendCompletionsRequest());

        inputField.text = String.Empty;
        
    }

    IEnumerator SendCompletionsRequest()  // Chat Completion Request
    {
        UnityWebRequest req = new UnityWebRequest(completionsURL, UnityWebRequest.kHttpVerbPOST);
        req.uploadHandler = (UploadHandler)new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(completionRequest)));
        req.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"Error: {req.error}");
        }
        else
        {
            var response = JsonUtility.FromJson<ChatCompletionResponse>(req.downloadHandler.text);
            foreach (var choice in response.choices)
            {
                Debug.Log($"role: {choice.message.role}, content: {choice.message.content}");
            }
            completionRequest.messages.Add(response.choices[0].message);
            StartCoroutine(SendCreateSpeechRequest(response.choices[0].message.content));
            CreateChatPanel(response.choices[0].message.content, Role.assistant, completionRequest.messages.Count - 1);
        }
    }

    void GetPcmAudio(float[] requestData)  // AudioClip Callback Function
    {
        lock (audioLock)  // thread safe
        {
            if (audioPosition >= audioData.Length)  // No audio data stock
            {
                Array.Fill(requestData, 0.0f);
                return;
            }

            if (audioPosition + requestData.Length > audioData.Length)  // Not enough stock of audio data.
            {
                var remainSize = audioData.Length - audioPosition;
                Array.Copy(audioData, audioPosition, requestData, 0, remainSize);
                Array.Fill<float>(requestData, 0.0f, remainSize, requestData.Length - remainSize);
                audioPosition = audioData.Length;
                return;
            }

            Array.Copy(audioData, audioPosition, requestData, 0, requestData.Length);  // Enough stock of audio data
            audioPosition += requestData.Length;
            return;
        }
    }

    IEnumerator SendCreateSpeechRequest(string text)  // Create Speech Request
    {
        var request = new CreateSpeechRequest(voice ,text);
        UnityWebRequest req = new UnityWebRequest(createSpeechURL, UnityWebRequest.kHttpVerbPOST);
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(request)));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.SetRequestHeader("Authorization", $"Bearer {apiKey}");

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"Error: {req.error}");
        }
        else
        {
            var receivedData = req.downloadHandler.data;

            Debug.Log($"Receive Audio Data: {receivedData.Length}");

            var task = CreateConvertAudioDataTask(receivedData);
        }
    }

    async Task CreateConvertAudioDataTask(byte[] inputAudio)
    {
        long elapsedTime = await Task.Run(() => ConvertAudioData(inputAudio));
        Debug.Log($"Convert Processing Time: {elapsedTime} ms");
    }

    long ConvertAudioData(byte[] inputAudio) 
    {
        Stopwatch sw = Stopwatch.StartNew();
        var audioData = new float[inputAudio.Length / 2];
        for (int i = 0; i < inputAudio.Length; i += 2)
        {
            var tmp = BitConverter.ToInt16(inputAudio, i);
            audioData[i / 2] = tmp / 32768.0f;
        }
        sw.Stop();
        
        lock (audioLock)  // thread safe
        {
            this.audioData = audioData;
            audioPosition = 0;
        }
        return sw.ElapsedMilliseconds;
    }
}
