# 영토 지도 바탕 (Natural Earth 1:10m)

출처: Natural Earth (naturalearthdata.com), 퍼블릭 도메인 — 육지 `ne_10m_land`, 호수 `ne_10m_lakes`, 강 `ne_10m_rivers_lake_centerlines` (v5.1.1).

만드는 법: `node tools/naturalearth_tiles.js <land.shp> <lakes.shp> <rivers.shp>` (셰이프파일은 저장소에 넣지 않는다).

형식: 10도 칸마다 `ne_<위도칸>_<경도칸>.json` = `{"land":[고리…],"lake":[고리…],"river":[선…]}`, 좌표는 1e-4도 정수, 첫 점 그대로 · 다음부터 차 (x = 경도, y = 위도).

세부 층 (2026-11-15, 한국 근처 칸 12~13 · 30~31만): `node tools/naturalearth_detail.js <셰이프파일 폴더>` — `ne_10m_admin_1_states_provinces_lines`(admin1 시도 경계 선), `ne_10m_roads`(road 고속도로 · 주요 도로 / road2 그 밖, 뱃길 뺌), `ne_10m_urban_areas`(urban 도시 지역 고리), `ne_10m_populated_places_simple`(place = [경도, 위도, 등급, 로마자 이름], 점은 차 없이). 모두 Natural Earth 1:10m 퍼블릭 도메인.
