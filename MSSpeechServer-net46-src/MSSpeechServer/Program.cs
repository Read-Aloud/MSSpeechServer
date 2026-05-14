using SimpleHttpServer;
using System;
using System.Collections.Generic;
using System.Net;
using SimpleHttpServer.Models;
using System.Threading;
using System.IO;
using Newtonsoft.Json;
using Microsoft.Speech.Synthesis;
using System.Runtime.InteropServices;

namespace MSSpeechServer
{
    internal class Program
    {
        static class Routes
        {
            public static List<Route> GET
            {
                get
                {
                    return new List<Route>()
                {
                    new Route()
                    {
                        Callable = GetVoicesHandler,
                        UrlRegex = "^\\/GetVoices$",
                        Method = "GET"
                    },
                    new Route()
                    {
                        Callable = SetTTSHandler,
                        UrlRegex = "^\\/SetTTS(?:\\?.*)?$",
                        Method = "GET"
                    },
                    new Route()
                    {
                        Callable = SetTTSHandler,
                        UrlRegex = "^\\/SetTTS(?:\\?.*)?$",
                        Method = "POST"
                    }
                };

                }
            }

            private static HttpResponse GetVoicesHandler(HttpRequest request)
            {
                // 获取可用的语音列表
                List<string> availableVoices = new List<string>();
                using (SpeechSynthesizer synth = new SpeechSynthesizer())
                {
                    foreach (InstalledVoice voice in synth.GetInstalledVoices())
                    {
                        VoiceInfo info = voice.VoiceInfo;
                        availableVoices.Add(info.Name);
                    }
                }

                // 构造 JSON 响应
                string jsonResponse = JsonConvert.SerializeObject(new { errcode = 0, errmsg = "", rtval = availableVoices });

                return new HttpResponse()
                {
                    ContentAsUTF8 = jsonResponse,
                    ReasonPhrase = "OK",
                    StatusCode = "200",
                    Headers = { ["Content-Type"] = "application/json" }
                };
            }

            private static HttpResponse SetTTSHandler(HttpRequest request)
            {
                Dictionary<string, string> queryParameters = GetQueryParameters(request.Url);
                string text = null;
                string voiceName = null;

                queryParameters.TryGetValue("voiceName", out voiceName);

                if (request.Method == "POST")
                {
                    text = request.Content;
                }
                else
                {
                    queryParameters.TryGetValue("text", out text);
                }

                // 检查参数是否为空
                if (string.IsNullOrWhiteSpace(text))
                {
                    // 构造 JSON 响应
                    string jsonResponse = JsonConvert.SerializeObject(new { errcode = 4001, errmsg = "Error: 'text' parameter cannot be empty", rtval = (string)null });

                    return new HttpResponse()
                    {
                        ContentAsUTF8 = jsonResponse,
                        ReasonPhrase = "Bad Request",
                        StatusCode = "400",
                        Headers = { ["Content-Type"] = "application/json" }
                    };
                }

                // Process the request to set TTS (Text-to-Speech) with text and voiceName
                using (SpeechSynthesizer synth = new SpeechSynthesizer())
                {
                    var voices = synth.GetInstalledVoices();
                    if (voices.Count == 0)
                    {
                        // 构造 JSON 响应
                        string jsonResponse = JsonConvert.SerializeObject(new { errcode = 4001, errmsg = "No available voices found.", rtval = (string)null });

                        return new HttpResponse()
                        {
                            ContentAsUTF8 = jsonResponse,
                            ReasonPhrase = "Bad Request",
                            StatusCode = "400",
                            Headers = { ["Content-Type"] = "application/json" }
                        };
                    }
                    else
                    {

                        // 如果设置语音库
                        bool makeSpeak = true;

                        if (!string.IsNullOrWhiteSpace(voiceName))
                        {
                            bool voiceFound = false;
                            foreach (InstalledVoice voice in voices)
                            {
                                VoiceInfo info = voice.VoiceInfo;
                                if (info.Name == voiceName)
                                {
                                    synth.SelectVoice(info.Name);
                                    voiceFound = true;

                                    break;
                                }
                            }
                            if (!voiceFound)
                            {
                                makeSpeak = false;
                                // 构造 JSON 响应
                                string jsonResponse = JsonConvert.SerializeObject(new { errcode = 4001, errmsg = $"Voice '{voiceName}' not found.", rtval = (string)null });

                                return new HttpResponse()
                                {
                                    ContentAsUTF8 = jsonResponse,
                                    ReasonPhrase = "Bad Request",
                                    StatusCode = "400",
                                    Headers = { ["Content-Type"] = "application/json" }
                                };
                            }
                        }
                        else
                        {
                            // 默认选择第一个语音
                            VoiceInfo info = voices[0].VoiceInfo;
                            synth.SelectVoice(info.Name);
                        }

                        if (makeSpeak)
                        {
                            // 将文本转换为语音并保存为 WAV 格式的字节数组
                            var memoryStream = new MemoryStream();
                            synth.SetOutputToWaveStream(memoryStream);
                            if (IsSsml(text))
                            {
                                synth.SpeakSsml(text);
                            }
                            else
                            {
                                synth.Speak(text);
                            }
                            memoryStream.Position = 0;

                            // 构造响应
                            return new HttpResponse
                            {
                                Content = memoryStream.ToArray(),
                                Headers = { ["Content-Type"] = "audio/wav" },
                                StatusCode = "200",
                                ReasonPhrase = "OK"
                            };
                        }

                    }
                }

                // Default response if no data is returned
                return new HttpResponse()
                {
                    ContentAsUTF8 = "",
                    ReasonPhrase = "OK",
                    StatusCode = "200"
                };
            }

            private static Dictionary<string, string> GetQueryParameters(string url)
            {
                Dictionary<string, string> parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                int queryStart = url.IndexOf('?');
                if (queryStart < 0 || queryStart == url.Length - 1)
                {
                    return parameters;
                }

                string queryString = url.Substring(queryStart + 1);
                string[] pairs = queryString.Split('&');
                foreach (string pair in pairs)
                {
                    if (string.IsNullOrEmpty(pair))
                    {
                        continue;
                    }

                    int separator = pair.IndexOf('=');
                    string key = separator >= 0 ? pair.Substring(0, separator) : pair;
                    string value = separator >= 0 ? pair.Substring(separator + 1) : "";

                    key = WebUtility.UrlDecode(key);
                    value = WebUtility.UrlDecode(value);
                    Console.WriteLine($"{key}: {value}");

                    parameters[key] = value;
                }

                return parameters;
            }

            private static bool IsSsml(string text)
            {
                return text.TrimStart().StartsWith("<speak", StringComparison.OrdinalIgnoreCase);
            }
        }

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.FirstChanceException += (sender, eventArgs) =>
            {
                if (eventArgs.Exception is COMException comEx)
                {
                    Console.WriteLine("First chance COMException: " + comEx.Message);
                    Console.WriteLine("ErrorCode: " + comEx.ErrorCode);
                    Console.WriteLine("StackTrace: " + comEx.StackTrace);
                }
            };

            HttpServer httpServer = new HttpServer(8080, Routes.GET);
            Console.WriteLine("HTTP server is started and waiting for connections...");
            Thread thread = new Thread(new ThreadStart(httpServer.Listen));
            thread.Start();
        }
    }
}
