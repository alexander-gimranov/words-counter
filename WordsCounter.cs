using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommandLine;

class Options
{
    [Option('l', "minLength", Default = 5, HelpText = "Minimum word length")]
    public int MinLength { get; set; }

    [Option('p', "path", Default = "", HelpText = "Folder path")]
    public string FolderPath { get; set; }

    [Option('pl', "parallelism", Default = 0, HelpText = "Max degree of parallelism")]
    public int MaxParallelism { get; set; }
}

class Program
{
    static void Main(string[] args)
    {
        Parser.Default.ParseArguments<Options>(args)
            .WithParsed(options =>
            {
                // Путь к папке с текстовыми файлами
                string folderPath = string.IsNullOrEmpty(options.FolderPath) ? Environment.CurrentDirectory : options.FolderPath;

                // Компиляция регулярного выражения один раз
                var wordPattern = new Regex(@"\b\w{" + (options.MinLength + 1) + @",}\b", RegexOptions.Compiled);

                // Получение списка файлов в папке
                var files = Directory.GetFiles(folderPath, "*.txt");

                // Словарь для хранения частотности слов
                ConcurrentDictionary<string, int> wordFrequency = new ConcurrentDictionary<string, int>();

                // Установка максимального количества параллельных потоков
                var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = options.MaxParallelism == 0 ? Environment.ProcessorCount - 1 : options.MaxParallelism }; 

                // Обработка файлов в многопоточном режиме
                Parallel.ForEach(files, parallelOptions, file =>
                {
                    using (var reader = new StreamReader(file))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            // Использование скомпилированного регулярного выражения для разбиения текста на слова
                            var words = wordPattern.Matches(line)
                                .Cast<Match>()
                                .Select(m => m.Value.ToLower());

                            foreach (var word in words)
                            {
                                wordFrequency.AddOrUpdate(word, 1, (key, oldValue) => oldValue + 1);
                            }
                        }
                    }
                });

                // Сортировка слов по частотности и выбор топ-10
                var topWords = wordFrequency.OrderByDescending(kvp => kvp.Value)
                    .Take(10);

                // Вывод результата
                Console.WriteLine($"Top 10 most frequently used words longer than {minLength} characters:");
                foreach (var kvp in topWords)
                {
                    Console.WriteLine($"{kvp.Key}: {kvp.Value}");
                }
            });
    }
}