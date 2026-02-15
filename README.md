# Word Game — gRPC клієнт-сервер
Вернік Михайло - КП-в41ф
<br>
Варіант 13

Мультиплеєрна гра: гравці отримують набір літер, перший валідний варіант виграє раунд.

**Потрібно:** .NET 9.0 SDK

**Запуск:**
```bash
cd WordGame
dotnet run --project WordGame.Server
```

 **http://localhost:5119** . Ввести ім’я - Connect - чекати літери - ввести слово - Submit (або Enter).
<img width="1879" height="1025" alt="image" src="https://github.com/user-attachments/assets/3e958998-35c4-4cfb-a44b-d2c021f22f15" />
<img width="1900" height="1029" alt="image" src="https://github.com/user-attachments/assets/eedc1c05-bd01-4d83-9178-35139361b6fc" />

**Структура:** WordGame.Server — ASP.NET Core (gRPC + Blazor).

**gRPC та Proto:** Контракт описано в `Protos/wordgame.proto`. Сервіс `WordGame` має 3 RPC: `JoinGame` (простий), `SubscribeToGame` (серверний стримінг — літери та результати), `SubmitWord` (простий). Повідомлення: JoinRequest/Response, SubscribeRequest, GameEvent (LettersOffered, RoundResult), SubmitWordRequest/Response.

**Схема взаємодії:**

```mermaid
sequenceDiagram
    participant C1 as Клієнт1
    participant C2 as Клієнт2
    participant S as Сервер

    C1->>S: JoinGame(name)
    C2->>S: JoinGame(name)
    S-->>C1: player_id
    S-->>C2: player_id

    C1->>S: SubscribeToGame(player_id)
    C2->>S: SubscribeToGame(player_id)

    Note over S: Раунд: літери A,E,R,T,S
    S->>C1: LettersOffered
    S->>C2: LettersOffered

    C1->>S: SubmitWord("star")
    S->>S: Валідація, перший валідний
    S->>C1: RoundResult(winner)
    S->>C2: RoundResult(winner)
```
