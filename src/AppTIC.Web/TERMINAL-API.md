# API de alocação do terminal

A API usa a sessão autenticada da aplicação e mantém um único estado em memória por processo. O estado perde-se quando a aplicação reinicia e não é partilhado entre instâncias. As atribuições são independentes dos dados GTFS e das circulações existentes.

Os minutos de chegada e partida devem usar a mesma origem temporal em todos os pedidos. Os intervalos são `[chegada, partida)`: um cais pode ser reutilizado exatamente no minuto da partida. Zonas e cais: `Lisboa` (1–4), `Fora` (5–8) e `Norte` (9–10). Só os autocarros marcados como atracados mantêm o cais fixo quando chega outro autocarro. Identificadores não podem ser reutilizados durante a vida do processo, mesmo após a partida.

Os endpoints exigem autenticação. Para cada sessão, obter `GET /api/terminal/antiforgery`, conservar o cookie antifalsificação recebido e enviar o campo `token` no cabeçalho `RequestVerificationToken` dos pedidos POST.

- `GET /api/terminal/estado`: lista os autocarros ativos e os respetivos cais.
- `POST /api/terminal/chegada`: recebe JSON, por exemplo `{"id":"BUS-1","zona":"Lisboa","chegadaMinutos":600,"partidaMinutos":620}`. Devolve a atribuição e todas as alocações ativas. Um conflito de capacidade ou identificador devolve HTTP 409 sem alterar o estado.
- `POST /api/terminal/atracar/{id}`: confirma a atracagem e fixa o cais.
- `POST /api/terminal/partida/{id}`: conclui o autocarro e liberta o cais para novas alocações.
