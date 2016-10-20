﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
//using Xtricate.Common.Logging;

namespace Xtricate.Core.Common
{
    public class Seq : IDisposable
    {
        //private static readonly ILog Log = LogProvider.GetLogger(typeof(Seq));
        private static Stack<string> _froms = new Stack<string>();
        private static string _title;
        private static bool _enabled;
        private static bool _loggingEnabled;
        private readonly string _returnDescription;

        public Seq(string from, string description = null, string returnDescription = null, string title = null,
            bool? enabled = null, bool? loggingEnabled = null)
        {
            if (enabled.HasValue) _enabled = enabled.Value;
            if (loggingEnabled.HasValue) _loggingEnabled = loggingEnabled.Value;
            if (!_enabled) return;

            _returnDescription = returnDescription;
            if (!string.IsNullOrEmpty(title)) _title = title;
            if (_froms.Count >= 1)
            {
                Steps.Add(new SeqStep
                {
                    Type = _froms.Peek() != from ? SeqStepType.Call : SeqStepType.CallSelf,
                    From = _froms.Peek(),
                    To = from,
                    Description = description
                });
                //if (_loggingEnabled) Log.Debug($"Seq [{Steps.Count}]: from '{from}' - '{description}'");
            }
            _froms.Push(from);
        }

        public static List<SeqStep> Steps { get; set; } = new List<SeqStep>();

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public static bool IsEnabled() => _enabled;

        public static Seq Call(string from, string description = null, string returnDescription = null,
            string title = null)
        {
            if (!_enabled) return null;
            return new Seq(from, description, returnDescription, title);
        }

        public static void Self(string description)
        {
            if (!_enabled) return;

            Steps.Add(new SeqStep
            {
                Type = SeqStepType.Self,
                From = _froms.Peek(),
                To = _froms.Peek(),
                Description = description
            });
            //if (_loggingEnabled) Log.Debug($"Seq [{Steps.Count}]: self - '{description}'");
        }

        public static void Note(string description)
        {
            if (!_enabled) return;

            Steps.Add(new SeqStep
            {
                Type = SeqStepType.Note,
                Description = description,
                From = _froms.Peek(),
                To = _froms.Peek()
            });
            //if (_loggingEnabled) Log.Debug($"Seq [{Steps.Count}]: note - '{description}'");
        }

        public static void Reset()
        {
            _froms = new Stack<string>();
            Steps = new List<SeqStep>();
            _title = null;
            //if (_loggingEnabled) Log.Debug($"Seq [{Steps.Count}]: reset");
        }

        public static string Render()
        {
            var sb = new StringBuilder();
            sb.Append($"\ntitle {_title}\n");
            if (Steps != null)
            {
                //if (_loggingEnabled) Log.Debug($"Seq: render #{Steps.Count} steps");
                foreach (var s in Steps)
                {
                    sb.Append(s.Render());
                }
            }
            return sb.ToString();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;
            if (!_enabled) return;

            if (_froms.Count >= 2)
            {
                var from = _froms.Pop();
                if (from != _froms.Peek())
                {
                    Steps.Add(new SeqStep
                    {
                        Type = SeqStepType.Return,
                        From = from,
                        To = _froms.Peek(),
                        Description = _returnDescription
                    });
                    //if (_loggingEnabled)
                        //Log.Debug($"Seq [{Steps.Count}]: return from '{from}' - '{_returnDescription}'");
                }
            }
        }

        public static string RenderDiagram(
            string fileName = null, string path = null, string style = "modern-blue" /*"qsd"*/, string format = "png")
        {
            return RenderDiagram(Render(), fileName, path, style, format);
        }

        /// <summary>
        ///     Given a WSD description, produces a sequence diagram PNG.
        /// </summary>
        /// <param name="fileName">Name of the file.</param>
        /// <param name="path">The path.</param>
        /// <param name="style">One of the valid styles for the diagram</param>
        /// <param name="format">The output format requested. Must be one of the valid format supported</param>
        /// <returns>
        ///     The full path of the downloaded image
        /// </returns>
        /// <exception cref="System.Exception">
        ///     Unexpected HTTP status from server:  + response.StatusCode + :  + response.StatusDescription
        ///     or
        ///     Error parsing response from server:  + jsonObject
        ///     or
        ///     Server reported HTTP error during image fetch:  + response.StatusCode + :  + response.StatusDescription
        ///     or
        ///     Exception while saving image to temp file:  + e.Message
        /// </exception>
        /// <exception cref="Exception">If an error occurred during the request</exception>
        /// This method uses the WebSequenceDiagrams.com public API to query an image and stored in a local
        /// temporary directory on the file system.
        /// You can easily change it to return the stream to the image requested instead of a file.
        /// To invoke it:
        /// ..
        /// using System.Web;
        /// ...
        /// string name = grabSequenceDiagram("a-&gt;b: Hello", "qsd", "png");
        /// ..
        /// You need to add the assembly "System.Net.Http" and "System.IO" to your reference list (that by default is not
        /// added to new projects)
        /// Questions / suggestions: fabriziobertocci@gmail.com
        private static string RenderDiagram(
            string wsd, string fileName = null, string path = null, string style = "modern-blue" /*"qsd"*/, string format = "png")
        {
            if (string.IsNullOrEmpty(wsd)) throw new ArgumentNullException(nameof(wsd));
            // Websequence diagram API:
            // prepare a POST body containing the required properties
            var requestContent = new StringBuilder("style=");
            requestContent.Append(style).Append("&apiVersion=1&format=").Append(format).Append("&message=");
            requestContent.Append(WebUtility.UrlEncode(WebUtility.UrlDecode(wsd)));

            using (var client = new HttpClient())
            {
                // Post the diagram data
                var result = client.PostAsync("http://www.websequencediagrams.com/index.php", new StringContent(requestContent.ToString())).Result;
                result.EnsureSuccessStatusCode();
                var responseJson = result.Content.ReadAsStringAsync().Result;
                if (responseJson != null)
                {
                    var components = responseJson.Split('"');
                    // Ensure component #1 is 'img':
                    if (components[1].Equals("img") == false) throw new Exception("Error parsing response from server: " + responseJson);
                    var uri = components[3];
                    // Get the image
                    result = client.GetAsync("http://www.websequencediagrams.com/" + uri).Result;
                    result.EnsureSuccessStatusCode();
                    using (var responseStream = result.Content.ReadAsStreamAsync().Result)
                    {
                        try
                        {
                            if (string.IsNullOrEmpty(fileName))
                            {
                                fileName = Path.GetTempFileName().Replace(".tmp", "." + format);
                            }
                            else
                            {
                                if (string.IsNullOrEmpty(path))
                                {
                                    fileName += "." + format;
                                }
                                else
                                {
                                    fileName = Path.Combine(path, fileName) + "." + format;
                                }
                            }
                            using (var destinationStream = new FileStream(fileName, FileMode.Create))
                            {
                                // Copy streams
                                var buffer = new byte[1024];
                                int read;
                                while ((read = responseStream.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    destinationStream.Write(buffer, 0, read);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            throw new Exception("Exception while saving image to temp file: " + e.Message);
                        }
                    }
                }
            }
            return fileName;
        }
    }
}

//public class Start : IDisposable
//{
//    private readonly string _returnDescription;

//    public Start(string title, string name, string returnDescription)
//    {
//        Sequence.Start(title, name);
//        _returnDescription = returnDescription;
//    }
//    public void Dispose()
//    {
//        Dispose(true);
//        GC.SuppressFinalize(this);
//    }

//    /// <summary>
//    /// Called when the object is cleaned up, to close the scope
//    /// </summary>
//    protected virtual void Dispose(bool disposing)
//    {
//        if (!disposing) return;
//        //Sequence.Return(_returnDescription);
//    }
//}

//public static class Sequence
//{
//    private static string _title;
//    private static string _activeTo;
//    private static string _activeFrom;
//    private static string _activeDescription;
//    private static readonly Stack<string> Froms = new Stack<string>();
//    public static List<Step> Steps = new List<Step>();

//    public static void Start(string title, string name)
//    {
//        _title = title;
//        Steps = new List<Step>();
//        _activeFrom = name;
//        Froms.Push(name);
//        _activeDescription = null;
//        _activeTo = null;
//    }

//    public static void From(string name, string description = null)
//    {
//        _activeFrom = name;
//        Froms.Push(name);
//        if (!string.IsNullOrEmpty(description)) _activeDescription = description;
//    }

//    public static void To(string name, string description)
//    {
//        _activeTo = name;
//        if (string.IsNullOrEmpty(_activeFrom)) From(name, description);
//        if (!string.IsNullOrEmpty(description)) _activeDescription = description;
//        Steps.Add(new Step { Type = StepType.Call, From = _activeFrom, To = name, Description = _activeDescription });
//    }

//    public static void ToSelf(string name, string description)
//    {
//        //_activeTo = name;
//        //if (!string.IsNullOrEmpty(description)) _activeDescription = description;
//        Steps.Add(new Step { Type = StepType.Self, From = name, To = name, Description = description });
//    }

//    public static void ToSelf(string description)
//    {
//        //_activeTo = name;
//        //if (!string.IsNullOrEmpty(description)) _activeDescription = description;
//        Steps.Add(new Step { Type = StepType.Self, From = _activeTo, To = _activeTo, Description = description });
//    }

//    public static void AddNote(string description)
//    {
//        Steps.Add(new Step { Type = StepType.Note, Description = description, From = _activeTo, To = _activeTo });
//    }

//    public static void Return(string description)
//    {
//        _activeFrom = Froms.Pop();
//        Steps.Add(new Step { Type = StepType.Return, From = _activeTo, To = _activeFrom, Description = description });
//        _activeTo = _activeFrom;
//    }

//    public static string Render()
//    {
//        var sb = new StringBuilder();
//        sb.Append(string.Format("\ntitle {0}\n", _title));
//        if (Steps != null)
//            foreach (var s in Steps)
//            {
//                sb.Append(s.Render());
//            }
//        return sb.ToString();
//    }
//}