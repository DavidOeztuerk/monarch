using Monarch.Cli;

var env = CliEnv.FromProcess();
var code = MonarchApp.Run(args, env);
try
{
    env.Out.Flush();
}
catch (IOException)
{
}

return code;
