# 게임 서버 컨테이너 (docs/impl/server.md 6장). 어느 클라우드든 컨테이너로 올린다.
# 빌드 (저장소 루트에서): docker build -f server/Game.Server/Dockerfile -t game-server .
# 실행: docker run -p 8080:8080 -v game-store:/data/store game-server
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /repo
COPY src/ src/
COPY server/ server/
RUN dotnet publish server/Game.Server/Game.Server.csproj -c Release -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out ./
COPY data/ ./data/
ENV ASPNETCORE_URLS=http://+:8080 \
    Game__DataRoot=/app/data \
    Game__StoreRoot=/data/store
# 저장 폴더는 app 사용자가 쓸 수 있게 (볼륨을 처음 붙이면 이 권한을 이어받는다)
RUN mkdir -p /data/store && chown -R app:app /data/store
VOLUME /data/store
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Game.Server.dll"]
