# 잔해 조각 LOD (45 fps 작업, 2026-11-19)

`Assets/OYM/Generated/Wreck/frag_NNNN.oymmesh`(LOD 없음, 799조각 820만 삼각형)의 LOD1(25 %)·LOD2(6 %)를 Blender로 구워
`Assets/OYM/Generated/WreckLod/`에 두고, 런타임 `OYM.WreckLods`가 씬 로드 때 LODGroup을 붙입니다(씬 수정 없음).
1,500삼각형 미만 조각은 LOD를 만들지 않습니다.

잔해를 다시 내보냈으면(`skybox/export_assets.py`) 아래를 다시 돌리세요. GUID는 파일 이름에서 만들어서 다시 돌려도 참조가 유지됩니다.

```bash
blender -b --factory-startup -P Tools/OYM/wreck_lods/make_lods.py -- Assets/OYM/Generated/Wreck Assets/OYM/Generated/WreckLod
python Tools/OYM/wreck_lods/make_table.py .
# 확인용 렌더: blender -b --factory-startup -P Tools/OYM/wreck_lods/preview.py -- out.png <lod0> <lod1> <lod2>
```

Unity 6.3 내장 `MeshLodUtility.GenerateMeshLods`는 `frag_0090`에서 에디터가 네이티브 크래시해서 쓰지 않습니다.
