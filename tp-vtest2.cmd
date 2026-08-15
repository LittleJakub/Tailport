@echo off
for /f "tokens=1,2,3 delims=<>" %%A in ('echo ^<Version^>1.8.0^</Version^>') do echo BOTH: A=[%%A] B=[%%B] C=[%%C]
for /f "tokens=1,2 delims=<" %%A in ('echo ^<Version^>1.8.0^</Version^>') do echo LT:   A=[%%A] B=[%%B]
for /f "tokens=1,2 delims=>" %%A in ('echo ^<Version^>1.8.0^</Version^>') do echo GT:   A=[%%A] B=[%%B]
for /f "tokens=2 delims=<>" %%V in ('echo Version^>1.8.0^</Version^>') do echo NOTOKEN: V=[%%V]
