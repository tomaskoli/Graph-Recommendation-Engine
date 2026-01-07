FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Recommendation.Api/Recommendation.Api.csproj src/Recommendation.Api/
COPY src/Recommendation.ServiceDefaults/Recommendation.ServiceDefaults.csproj src/Recommendation.ServiceDefaults/
RUN dotnet restore src/Recommendation.Api/Recommendation.Api.csproj

COPY src/Recommendation.Api/ src/Recommendation.Api/
COPY src/Recommendation.ServiceDefaults/ src/Recommendation.ServiceDefaults/
RUN dotnet publish src/Recommendation.Api/Recommendation.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "Recommendation.Api.dll"]

