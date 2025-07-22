

// echo stdin to stdout    
string? line;
while ((line = Console.ReadLine()) != null)
{
    File.AppendAllText(".\\actor2.log", line.Replace("\\n", Environment.NewLine));
    File.AppendAllText(".\\actor2.log", $"{Environment.NewLine}**************{Environment.NewLine}");
    Console.WriteLine(DateTime.Now);
}
