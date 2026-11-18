# OnYourMark 협동 콜아웃 음성 (m8b 최종본)

두 사람의 HUD가 서로 바뀌어 있는 협동 모드에서, 상대에게 체력 · 연료 · 탄약 · 적 위치를 말로 알려주는 영어 대사 33줄입니다.

## 바로 듣기
1. zip을 **먼저 압축 해제**하세요 (zip 안에서 바로 열면 소리가 안 나요).
2. `listen.html`을 크롬이나 엣지로 열면 줄마다 재생 버튼이 있어요.

## 목소리
- 남성 바리톤, 평소 음높이 약 130Hz. 우주 군인 분대원 캐릭터, 무전 콜아웃 톤.
- 줄마다 톤을 맞춰 녹음: **외침**(급함 · 외침) / **단호**(단호 · 긴장 · 신남) / **차분**(차분 · 안도 · 머쓱)
- AI 음성 합성(Qwen3-TTS)으로 만든 목소리예요. 문장 묘사로 새 목소리를 설계한 것이라 실존 인물의 목소리를 복제한 게 아니에요.

## 파일 형식
- `NN_<id>.wav` — 44.1kHz · 16bit · 모노, 음량 약 -16 LUFS로 맞춤, 앞 30ms · 뒤 150ms 여백
- `ref_shout.wav` · `ref_firm.wav` · `ref_calm.wav` — 톤 기준 음성 (게임에는 안 쓰는 참고용)
- `lines.csv` — 대사표 (엑셀로 열림)

## 참고할 점
- 키 이름(E 힐팩, R 재장전, Q 스킬)은 임시예요. 키 배치가 바뀌면 해당 줄만 다시 뽑으면 돼요.
- 24번 `24_go.wav`는 대본상 "Go, go, go!"인데 "Go! Go!"로 두 번만 들려요.

### 상대 HUD 읽어주기
| # | 파일 | 대사 | 뜻 | 톤 |
|---|---|---|---|---|
| 1 | `01_heal_now.wav` | Your health's dropping fast! Press E, use your heal pack! | 체력 빠르게 빠지고 있어! E 눌러, 힐팩 써! | 급함 |
| 2 | `02_heal_crit.wav` | You're in the red! Heal right now, hit E! | 빨간불이야! 당장 회복해, E 눌러! | 외침 |
| 3 | `03_heal_ok.wav` | Okay, you're good. Health's back up. | 좋아, 이제 괜찮아. 체력 돌아왔어. | 안도 |
| 4 | `04_shield_down.wav` | Your shield's down! Get behind cover! | 보호막 깨졌어! 엄폐해! | 급함 |
| 5 | `05_shield_up.wav` | Shield's back online. You're covered. | 보호막 다시 켜졌어. 이제 버틸 수 있어. | 차분 |
| 6 | `06_fuel_low.wav` | Your booster fuel is almost empty. Stop boosting! | 부스터 연료 거의 바닥이야. 부스터 그만 써! | 단호 |
| 7 | `07_fuel_full.wav` | Fuel's full. You're clear to dash. | 연료 꽉 찼어. 대시해도 돼. | 차분 |
| 8 | `08_ammo_low.wav` | You're almost out of ammo. Reload, press R! | 탄약 거의 없어. 재장전해, R 눌러! | 급함 |
| 9 | `09_ammo_empty.wav` | Your mag's empty! Reload, reload! | 탄창 비었어! 장전, 장전! | 외침 |
| 10 | `10_rail_ready.wav` | Railgun's fully charged. Take the shot! | 레일건 충전 완료. 쏴! | 단호 |
| 11 | `11_overheat.wav` | Your gun's overheating. Ease off the trigger. | 총 과열이야. 방아쇠 좀 떼. | 차분 |
| 12 | `12_skill_ready.wav` | Your Q is ready. Use it now! | Q 준비됐어. 지금 써! | 급함 |
| 13 | `13_skill_cd.wav` | Q's still on cooldown. Five more seconds. | Q 아직 쿨타임이야. 5초 남았어. | 차분 |

### 내 상태 물어보기
| # | 파일 | 대사 | 뜻 | 톤 |
|---|---|---|---|---|
| 14 | `14_ask_hp.wav` | How's my health? Read it to me. | 내 체력 어때? 읽어줘. | 긴장 |
| 15 | `15_ask_fuel.wav` | How much fuel do I have left? | 나 연료 얼마나 남았어? | 긴장 |
| 16 | `16_ask_ammo.wav` | Check my ammo for me. | 내 탄약 좀 봐줘. | 차분 |
| 17 | `17_ask_map.wav` | Where's the objective? You've got my map. | 목표 어디야? 내 지도 너한테 있잖아. | 차분 |
| 18 | `18_ask_again.wav` | Say again? I didn't catch that. | 다시 말해줘, 못 들었어. | 차분 |

### 적 · 레이더
| # | 파일 | 대사 | 뜻 | 톤 |
|---|---|---|---|---|
| 19 | `19_enemy_behind.wav` | Contact behind you! Turn around! | 뒤에 적! 돌아봐! | 외침 |
| 20 | `20_enemy_above.wav` | Incoming from above! | 위에서 온다! | 외침 |
| 21 | `21_enemy_big.wav` | Big one on your radar. It's closing in! | 네 레이더에 큰 놈 있어. 다가오고 있어! | 급함 |
| 22 | `22_enemy_clear.wav` | Radar's clear. We're safe for now. | 레이더 깨끗해. 일단 안전해. | 차분 |
| 23 | `23_weak_point.wav` | Hit the glowing spot. That's its weak point! | 빛나는 데 쏴. 거기가 약점이야! | 단호 |

### 팀플
| # | 파일 | 대사 | 뜻 | 톤 |
|---|---|---|---|---|
| 24 | `24_go.wav` | Go, go, go! I've got your back. | 가, 가, 가! 뒤는 내가 맡을게. | 외침 |
| 25 | `25_hold.wav` | Hold up. Wait for my call. | 잠깐. 내 신호 기다려. | 단호 |
| 26 | `26_stay_close.wav` | Stay on me. Don't drift off. | 나한테 붙어. 떠내려가지 마. | 단호 |
| 27 | `27_downed.wav` | I'm down! Get over here and pick me up! | 나 쓰러졌어! 와서 일으켜줘! | 외침 |
| 28 | `28_reviving.wav` | Hang on, I'm coming to get you. | 버텨, 지금 구하러 갈게. | 급함 |
| 29 | `29_thanks.wav` | Thanks. That was close. | 고마워. 큰일 날 뻔했어. | 안도 |
| 30 | `30_copy.wav` | Copy that. | 알았어. | 차분 |
| 31 | `31_negative.wav` | Negative. Not yet. | 아니, 아직이야. | 단호 |
| 32 | `32_nice_shot.wav` | Nice shot! | 나이스 샷! | 신남 |
| 33 | `33_my_bad.wav` | My bad. That one's on me. | 미안, 내 실수야. | 머쓱 |
