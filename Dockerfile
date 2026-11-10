FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY global.json ./
COPY smart-locking-be.API/*.csproj smart-locking-be.API/
COPY smart-locking-be.Application/*.csproj smart-locking-be.Application/
COPY smart-locking-be.Domain/*.csproj smart-locking-be.Domain/
COPY smart-locking-be.Infrastructure/*.csproj smart-locking-be.Infrastructure/
RUN dotnet restore smart-locking-be.API/smart-locking-be.API.csproj
COPY . .
RUN dotnet publish smart-locking-be.API/smart-locking-be.API.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
COPY supabase-ca.crt .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "smart-locking-be.API.dll"]
