FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Pitako.sln ./
COPY src/BuildingBlocks src/BuildingBlocks
COPY src/Modules src/Modules
COPY src/Hosts/Pitako.Api src/Hosts/Pitako.Api

RUN dotnet restore src/Hosts/Pitako.Api/Pitako.Api.csproj
RUN dotnet publish src/Hosts/Pitako.Api/Pitako.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV DatabaseProvider=PostgreSql

EXPOSE 10000
ENTRYPOINT ["dotnet", "Pitako.Api.dll"]
