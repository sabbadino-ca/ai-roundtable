// echo stdin to stdout    
string? line;
while ((line = Console.ReadLine()) != null)
{
    File.AppendAllText(".\\actor1.log",line.Replace("\\n",Environment.NewLine));
    File.AppendAllText(".\\actor1.log", $"{Environment.NewLine}**************{Environment.NewLine}");
    Console.WriteLine(DateTime.Now);
}