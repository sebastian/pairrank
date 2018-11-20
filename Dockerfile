FROM microsoft/dotnet:2.1-sdk AS build
WORKDIR /app

# copy csproj and restore as distinct layers
COPY *.sln .
COPY PairRankWeb/*.fsproj ./PairRankWeb/
RUN dotnet restore

# copy everything else and build app
COPY PairRankWeb/. ./PairRankWeb//
WORKDIR /app/PairRankWeb
RUN dotnet publish -c Release -o out


FROM microsoft/dotnet:2.1-aspnetcore-runtime
WORKDIR /app
COPY --from=build /app/PairRankWeb/out ./
ENTRYPOINT ["dotnet", "PairRankWeb.dll"]
