"""Launch the standalone C# server used by runtime acceptance probes."""
from pathlib import Path
import shutil

def server_command(dotnet=None):
    host=dotnet or (str(Path('.cache/dotnet/dotnet.exe').resolve()) if Path('.cache/dotnet/dotnet.exe').exists() else shutil.which('dotnet'))
    dll=Path('server/bin/Release/net8.0/SGTest.Server.dll').resolve()
    if not host or not dll.exists():
        raise RuntimeError('Build first: dotnet build server/SGTest.Server.csproj -c Release (.NET 8 SDK)')
    return [host,str(dll)]
