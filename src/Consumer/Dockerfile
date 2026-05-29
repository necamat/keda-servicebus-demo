FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Consumer/Consumer.csproj", "src/Consumer/"]
RUN dotnet restore "src/Consumer/Consumer.csproj"
COPY . .
WORKDIR "/src/src/Consumer"
RUN dotnet build "Consumer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Consumer.csproj" -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Consumer.dll"]