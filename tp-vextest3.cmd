@echo off
for /f "tokens=1,2 delims==" %%V in ('echo a=b=c') do echo T12: V=[%%V] W=[%%W]
for /f "tokens=1,* delims==" %%V in ('echo a=b=c') do echo T1S: V=[%%V] W=[%%W]
for /f "tokens=1,* delims==" %%V in ('echo     ^<Version^>1.8.1^</Version^>') do echo ANGL: V=[%%V] W=[%%W]
