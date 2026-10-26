# Website + SignalR-hub in één container. Bouwt met de .NET 10 SDK, draait op de kleinere ASP.NET-runtime.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/CollectiveNarratives.Web/CollectiveNarratives.Web.csproj src/CollectiveNarratives.Web/
RUN dotnet restore src/CollectiveNarratives.Web/CollectiveNarratives.Web.csproj
COPY src/ src/
RUN dotnet publish src/CollectiveNarratives.Web -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Niet als root draaien. Poort 8080 is alleen bereikbaar via de Cloudflare Tunnel (zie deploy/docker-compose.yml).
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "CollectiveNarratives.Web.dll"]
