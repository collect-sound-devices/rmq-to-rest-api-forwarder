RambbitMQ To REST API Forwarder - Release Notes 
=====================================
~~~
Copyright 2025 - 2026 by Eduard Danziger
~~~

$version$
--------
~~~
Released on $date$
~~~

## Change
- Generic DebounceWorker, similar to one, used in the KafkaToRestApiForwarder
- Logging runtime version, build information and operating system details at startup
- .NET 8 -> .NET 10 upgrade
- README: improvements, clarifications, diagrams
- appsettings.Docker.json removed. The Docker-specific environment variables are now directly in the deployed docker-compose.yml


3.4.0
--------
~~~
Released on 18.12.2025
~~~

## Change
- Configuration extended by attempt number and delay between attempts

## New
- Event delivery attempts via additional RabbitMQ queue
- GitHub Codespace, if configured, will be awaken, if not running
- VolumeChangeEvent's debouncing implemented. Default interval is 400 milliseconds, configurable in appsettings.json


3.3.5
--------
~~~
Released on 05.09.2025
~~~

## New
- Separated from SoundWinAgent repository
- Logging to file, configured in appsettings.json
- ApiBaseUrl: Target added to appsettings.json. Possible values: Azure, Local, Codespace
