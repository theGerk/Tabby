using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommandLine;

namespace Tabby
{
	class Program
	{
		class ProgramArgs
		{

			[Option('r', "recursive", HelpText = "Include to only Tabby the local files.")]
			public bool Recursive { get; set; }


			[Option('t', "tab-size", Required = false, HelpText = "Set the tab size. Defaults to 4, unless smart option is included in which case it defaults to 1.")]
			public uint? TabSize { get; set; }


			[Option('s', "smart", HelpText = "Infers indentation level from information given")]
			public bool Smart { get; set; }

			//[Option("make-spaces", HelpText = "Converts to spaces rather than to tabs.")]
			//public bool Spaces { get; set; }


			[Value(0, Required = true, HelpText = "File or files to match", Min = 1)]
			public IEnumerable<string> Files { get; set; }


			[Option('d', "home-directory", Required = false, HelpText = "Used as starting directory", Default = ".")]
			public string StartingDirectory { get; set; }

			[Option('v', "verbose", HelpText = "Gives updates on what its doing", Default = false)]
			public bool Verbose { get; set; }
		}

		static void Main(string[] args)
		{
			var x = Parser.Default.ParseArguments<ProgramArgs>(args).WithParsed(options =>
			{
				var stopwatch = new Stopwatch();
				stopwatch.Start();
				var files = new HashSet<string>(options.Files.Select(file => Directory.GetFiles(options.StartingDirectory, file, options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)).Aggregate<IEnumerable<string>>((a, b) => a.Concat(b)));
				if (options.Verbose)
					Console.WriteLine($"Matched {files.Count()} files.");
				var tasks = files.Select(async fileName =>
				{
					if (options.Verbose)
						Console.WriteLine($"Start reading: {fileName}");
					var text = await File.ReadAllLinesAsync(fileName);
					if (options.Verbose)
						Console.WriteLine($"Start processing: {fileName}");
					bool isChanged = false;
					if (options.Smart)
					{
						var output = SmartTabbing(text, options.TabSize ?? 1);
						isChanged = text.Where((val, idx) => val != output[idx]).Any();
						text = output;
					}
					else
					{
						for (int i = 0; i < text.Length; i++)
						{
							var line = text[i];
							var spacing = getInitialSpacing(line, options.TabSize ?? 4);
							var content = line.TrimStart();
							text[i] = makeSpacing(spacing, options.TabSize ?? 4) + content;
							if (text[i] != line)
								isChanged = true;
						}
					}
					if (isChanged)
					{
						File.Delete(fileName);
						await File.WriteAllLinesAsync(fileName, text);
					}
					if (options.Verbose)
						if (isChanged)
							Console.WriteLine($"Completed and rewrote: {fileName}");
						else
							Console.WriteLine($"No changes made on: {fileName}");
					return isChanged;
				}).ToArray();
				Task.WhenAll(tasks).Wait();
				var end = DateTime.Now;
				stopwatch.Stop();
				Console.WriteLine($"Changed {tasks.Where(x => x.Result).Count()} files out of {tasks.Length} matched files in {stopwatch.ElapsedMilliseconds} ms.");
			});
		}

		private static string makeSpacing(uint spacing, uint tabSize)
		{
			var tabs = spacing / tabSize;
			var extraSpaces = spacing % tabSize;
			return new string('\t', (int)tabs) + new string(' ', (int)extraSpaces);
		}

		static uint getInitialSpacing(string line, uint tabSize)
		{
			uint spacing = 0;
			foreach (var character in line)
			{
				switch (character)
				{
					case ' ':
						spacing += 1;
						break;
					case '\t':
						spacing /= tabSize;
						spacing += 1;
						spacing *= tabSize;
						break;
					default:
						return spacing;
				}
			}
			return spacing;
		}

		static string[] SmartTabbing(string[] file, uint tabsize = 1)
		{
			var spacings = new uint[file.Length];
			for (int i = 0; i < file.Length; i++)
			{
				if (file[i].TrimStart().Length == 0)
					spacings[i] = i == 0 ? 0 : spacings[i - 1];
				else
					spacings[i] = getInitialSpacing(file[i], tabsize);
			}
			var parentLine = new int[file.Length];
			var stack = new Stack<int>();
			stack.Push(-1);
			Func<int, uint> getSpacing = (int lineNumber) => lineNumber == -1 ? 0 : spacings[lineNumber];
			for (int i = 0; i < spacings.Length; i++)
			{
				//move stack to appropriate state and capture the current parent
				int parent = stack.Pop();
				while (getSpacing(parent) > spacings[i])
					parent = stack.Pop();
				//put the current parent back if it may be needed again
				if (getSpacing(parent) < spacings[i])
					stack.Push(parent);
				//store who the parent is
				parentLine[i] = parent;
				stack.Push(i);
			}
			uint[] hits = new uint[file.Length];
			uint[] newIndentations = new uint[file.Length];
			for (int i = file.Length - 1; i >= 0; i--)
			{
				//if me and my parent have the same spacing then I don't indent any further than it does.
				if (getSpacing(parentLine[i]) == spacings[i])
					newIndentations[i] = 0;
				else
				{
					//otherwise I indent off by 1 from my parent for each row below me that shares this as a parent (including myself, but not including anything that has the same indentation as the parent)
					newIndentations[i] = ++hits[parentLine[i] + 1]; //note that the +1 is used as it is possible to have a parentLine of -1 if there is no parent and it is not possible to have a parent line of yourself so the last line can't have any children.
				}
			}
			string[] outputFile = new string[file.Length];
			for (int i = 0; i < file.Length; i++)
			{
				spacings[i] = getSpacing(parentLine[i]) + newIndentations[i];
				outputFile[i] = makeSpacing(spacings[i], 1) + file[i].TrimStart();
			}
			return outputFile;
		}
	}
}