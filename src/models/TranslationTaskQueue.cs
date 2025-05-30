﻿using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator.models
{
    public class TranslationTaskQueue
    {
        private readonly object _lock = new object();
        private readonly List<TranslationTask> tasks;

        private (string translatedText, bool isChoke) output;
        public (string translatedText, bool isChoke) Output => output;

        public TranslationTaskQueue()
        {
            tasks = new List<TranslationTask>();
            output = (string.Empty, false);
        }

        public void Enqueue(Func<CancellationToken, Task<(string, bool)>> worker, string originalText)
        {
            var newTranslationTask = new TranslationTask(worker, originalText, new CancellationTokenSource());
            lock (_lock)
            {
                tasks.Add(newTranslationTask);
            }
            // Run `OnTaskCompleted` in a new thread.
            newTranslationTask.Task.ContinueWith(
                task => OnTaskCompleted(newTranslationTask),
                TaskContinuationOptions.OnlyOnRanToCompletion
            );
        }

        private async Task OnTaskCompleted(TranslationTask translationTask)
        {
            lock (_lock)
            {
                var index = tasks.IndexOf(translationTask);
                for (int i = 0; i < index; i++)
                    tasks[i].CTS.Cancel();
                for (int i = index; i >= 0; i--)
                    tasks.RemoveAt(i);
            }
            
            output = translationTask.Task.Result;
            var translatedText = output.Item1;
            
            // Log after translation.
            bool isOverwrite = await Translator.IsOverwrite(translationTask.OriginalText);
            if (!isOverwrite)
                await Translator.AddLogCard();
            await Translator.Log(translationTask.OriginalText, translatedText, isOverwrite);
        }
    }

    public class TranslationTask
    {
        public Task<(string, bool)> Task { get; }
        public string OriginalText { get; }
        public CancellationTokenSource CTS { get; }

        public TranslationTask(Func<CancellationToken, Task<(string, bool)>> worker,
            string originalText, CancellationTokenSource cts)
        {
            Task = worker(cts.Token);
            OriginalText = originalText;
            CTS = cts;
        }
    }
}