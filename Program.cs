using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using CommandLine;

class Options
{
    [Option('l', "minLength", Default = 5, HelpText = "Minimum word length")]
    public int MinLength { get; set; }

    [Option('p', "path", Default = "", HelpText = "Folder path")]
    public string? FolderPath { get; set; }

    [Option('c', "parallelism", Default = 0, HelpText = "Max degree of parallelism")]
    public int MaxParallelism { get; set; }
}

class Program
{
    private static readonly ManualResetEvent evtNewFile = new ManualResetEvent(false);
    private static readonly ManualResetEvent evtAllFiles = new ManualResetEvent(false);

    private static readonly Dictionary<string, int> allWordsFrequency = new Dictionary<string, int>();

    private static readonly ConcurrentQueue<Dictionary<string, int>> queue = new ConcurrentQueue<Dictionary<string, int>>();

    private static readonly Thread messageProcessorThread = new Thread(MessageProcessor);

    static void Main(string[] args)
    {
        Parser.Default.ParseArguments<Options>(args)
            .WithParsed(options =>
            {
                // Путь к папке с текстовыми файлами
                string folderPath = string.IsNullOrEmpty(options.FolderPath) ? Environment.CurrentDirectory : options.FolderPath;

                // Компиляция регулярного выражения один раз
                var wordPattern = new Regex(@"\w{" + options.MinLength + @",}", RegexOptions.Compiled);

                // Получение списка файлов в папке
                var files = Directory.GetFiles(folderPath, "*.txt");

                // Установка максимального количества параллельных потоков
                var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = options.MaxParallelism == 0 ? Environment.ProcessorCount - 1 : options.MaxParallelism }; 

                messageProcessorThread.Start();

                // Обработка файлов в многопоточном режиме
                Parallel.ForEach(files, parallelOptions, file =>
                {
                    using (var reader = new StreamReader(file))
                    {
                        string? line;
                        var wordsFrequency = new Dictionary<string, int>();
                        while ((line = reader.ReadLine()) != null)
                        {
                            // Использование скомпилированного регулярного выражения для разбиения текста на слова
                            var words = wordPattern.Matches(line)
                                .Cast<Match>()
                                .Select(m => m.Value.ToLower());

                            foreach (var word in words)
                            {
                                if (wordsFrequency.TryGetValue(word, out var count))
                                {
                                    wordsFrequency[word] = count + 1;
                                }
                                else
                                {
                                    wordsFrequency.Add(word, 1);
                                }
                            }
                        }

                        // Добавляем статистику файла в очередь
                        queue.Enqueue(wordsFrequency);

                        // Сигнализируем, что новыя статистика файла добавлена
                        evtNewFile.Set();
                    }
                });

                // Сигнализируем, что все файлы обработаны
                evtAllFiles.Set();

                var outputStatisticsThread = new Thread(OutputStatistics);
                outputStatisticsThread.Start();
            });
    }

    private static void MessageProcessor()
    {
        while (true)
        {
            // Ожидаем сигнал о новом файле или завершении всех файлов
            int index = WaitHandle.WaitAny(new WaitHandle[] { evtNewFile, evtAllFiles });

            // Проверяем, какой сигнал получен
            if (index == 0) // evtNewFile
            {
                // Обрабатываем все файлы в очереди
                ProcessFileStatistics();
                
                // Сбрасываем событие, чтобы ждать следующего файла
                evtNewFile.Reset();
            } else if (index == 1) // evtAllFiles
            {
                // Обрабатываем оставшиеся файлы в очереди
                ProcessFileStatistics();
                
                evtAllFiles.Reset();
                
                break;
            }

            Thread.Sleep(200);
        }
    }

    private static void ProcessFileStatistics()
    {
        while (queue.TryDequeue(out var wordsFrequency))
        {
            foreach (var (word, frequency) in wordsFrequency)
            {
                if (allWordsFrequency.TryGetValue(word, out var oldValue))
                {
                    allWordsFrequency[word] = oldValue + frequency;
                }
                else
                {
                    allWordsFrequency.Add(word, frequency);
                }
            }
        }
    }

    private static void OutputStatistics()
    {
        if (messageProcessorThread.ThreadState != ThreadState.Unstarted)
            messageProcessorThread.Join();

        // Сортировка слов по частотности и выбор топ-10
        var topWords = allWordsFrequency
                        .OrderByDescending(kvp => kvp.Value)
                        .Take(10);

        // Вывод результата
        Console.WriteLine($"10 most frequently used words:");
        foreach (var word in topWords)
        {
            Console.WriteLine($"{word.Key}: {word.Value}");
        }
    }
}
