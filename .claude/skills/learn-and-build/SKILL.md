---
name: learn-and-build
description: Quy trình bắt buộc mỗi khi code một phần mới trong MiniMart mà người dùng đang học song song (Transaction, Concurrency, Design Pattern, LINQ, DI, Auth...). Kích hoạt khi người dùng nói "bắt đầu Phase X", "code phần Y", hoặc bất kỳ yêu cầu implement tính năng nào liên quan đến kiến thức họ đang học. KHÔNG áp dụng cho sửa lỗi nhỏ, refactor không phát sinh kiến thức mới, hoặc thay đổi thuần thao tác (đổi tên biến, format code).
---

# Learn-and-Build Workflow - MiniMart

## Mục đích
Người dùng muốn Claude Code trực tiếp viết code (không tự gõ tay), nhưng đang học song song lý thuyết (LINQ, SOLID, DI, EF Core/Dapper, Auth, Transaction...). Mục tiêu kép: code chạy đúng **VÀ** người dùng hiểu rõ code, hiểu rõ vì sao làm theo cách đó.

## Khi nào dùng
- Người dùng yêu cầu implement một tính năng/Phase mới có liên quan đến kiến thức đang học.
- **Bắt buộc** với các chủ đề: Transaction, Concurrency/Race Condition, Design Pattern (Repository/Service), LINQ nâng cao, Dependency Injection, Authentication/Authorization.

## Khi nào KHÔNG dùng (bỏ qua workflow đầy đủ)
- Sửa lỗi cú pháp, lỗi build đơn giản.
- Refactor thuần (đổi tên, gộp file) không phát sinh kiến thức mới.
- Lặp lại y hệt pattern đã giải thích ở Phase trước → rút gọn Bước 2 và 4, chỉ cần nhắc "áp dụng như lần trước".

## Quan hệ với các skill khác (đọc trước khi chạy Bước 2)

Skill này là **khung quy trình bên ngoài**, không phải nơi chứa kiến thức kỹ thuật. Khi nội dung Phase rơi vào phạm vi một skill chuyên biệt, **gọi skill đó** ở Bước 2/3 thay vì tự diễn giải lại — tránh hai nguồn hướng dẫn nói lệch nhau:

| Nội dung Phase | Skill phụ trách Bước 2/3 |
|---|---|
| Trừ tồn kho, đặt hàng, race condition | `concurrency-check` |
| Tạo Repository/Service mới | `repository-pattern` |
| Entity thay đổi, cần migration | `ef-core-migration` |

Khung 7 bước (đặc biệt Bước 1, 4, 5, 6, 7) vẫn giữ nguyên; chỉ phần nội dung kỹ thuật là giao lại cho skill chuyên biệt.

---

## Quy trình 7 bước — tuần tự, không bỏ bước với chủ đề khó

### Bước 1 — Xác nhận phạm vi học nhanh
Liệt kê ngắn gọn 3-5 gạch đầu dòng các khái niệm lý thuyết liên quan đến phần sắp code, để người dùng tự đọc nhanh 5-10 phút nếu muốn. Chỉ nêu **tên khái niệm + 1 câu mô tả**, không giải thích sâu ở bước này.

### Bước 2 — Giải thích ý tưởng/thiết kế TRƯỚC khi code
Bắt buộc với: Transaction, Concurrency, Pattern, kiến trúc mới.

- Vấn đề đang giải quyết là gì, tại sao cần giải pháp này.
- Nếu có nhiều cách tiếp cận (VD: Optimistic vs Pessimistic Concurrency), trình bày **cả hai** và lý do chọn phương án cụ thể cho case này.
- **KHÔNG viết code ở bước này.** Dừng lại chờ xác nhận nếu người dùng muốn hỏi thêm trước khi qua Bước 3.

### Bước 3 — Code
Viết code đầy đủ theo đúng kiến trúc/quy ước trong `CLAUDE.md`. Comment ngắn tại các đoạn logic quan trọng; không comment thừa cho code hiển nhiên.

### Bước 4 — Walkthrough
Đi qua code vừa viết theo **từng khối logic** (không phải từng dòng), giải thích:
- Khối này làm gì.
- Vì sao viết theo cách này (liên hệ lại khái niệm ở Bước 1/2).
- Điểm nào dễ gây bug nếu làm sai.

### Bước 5 — Tự confirm (ĐIỂM DỪNG BẮT BUỘC)
Hỏi người dùng tóm tắt lại **bằng lời của họ** những gì vừa học/hiểu (1-2 câu).

**Quy tắc thực thi — đây là bước dễ bị làm hỏng nhất:**
- Hỏi xong thì **KẾT THÚC LƯỢT** và chờ người dùng trả lời thật. Tuyệt đối không tự trả lời thay, không đoán trước câu trả lời, không chạy tiếp Bước 6/7 trong cùng lượt. Nếu chạy tiếp, bước này thành hình thức và còn tệ hơn không có, vì nó tạo cảm giác đã kiểm tra trong khi chưa kiểm tra gì.
- Khi người dùng tóm tắt: xác nhận đúng/sai **có căn cứ**, chỉ rõ và sửa lại chỗ hiểu nhầm. Không nói "đúng rồi" cho qua.
- **Điều khoản thoát**: nếu người dùng bảo "bỏ qua", "đi tiếp", hoặc phớt lờ câu hỏi → đi tiếp Bước 6 ngay, không hỏi lại lần hai.

### Bước 6 — Test
Viết Unit Test cho phần vừa code, giải thích ngắn gọn test đang kiểm tra case nào và tại sao case đó quan trọng.

**Cảnh báo bắt buộc với test Concurrency/Race Condition** — không được viết test "giả lập" bằng mock cho loại này:
- **Moq**: mock `IProductRepository` thì không có tranh chấp thật nào xảy ra — chỉ đang test chính cái mock.
- **EF Core InMemory provider**: KHÔNG thực thi concurrency token (không ném `DbUpdateConcurrencyException`), và không chạy được raw SQL / `UPDLOCK`.

Cả hai cách trên đều cho test **luôn xanh kể cả khi code có bug oversell** → tệ hơn là không viết test. Test race condition thật phải:
1. Chạy trên **LocalDB / SQL Server thật** (integration test), không phải InMemory.
2. Bắn N request song song bằng `Task.WhenAll`.
3. Assert: tồn kho không bao giờ âm, và số đơn thành công đúng bằng số hàng thực có.

Nếu chưa dựng được integration test, nói thẳng là chưa test được phần concurrency — không thay bằng unit test giả.

### Bước 7 — Cập nhật CLAUDE.md rồi Commit

**7a. Cập nhật `CLAUDE.md` — BẮT BUỘC, không phải tuỳ chọn.**

Trước khi soạn commit, rà lại phần vừa làm và bổ sung vào `CLAUDE.md` mọi thứ thuộc các nhóm sau:
- Quy ước đặt tên hoặc cấu trúc mới (VD: `GetByIdAsync` vs `GetForUpdateAsync`).
- Quyết định kiến trúc đã chốt và KHÔNG được làm khác đi ở phase sau (VD: `SaveChangesAsync` chỉ nằm ở `IUnitOfWork`).
- Cạm bẫy đã gặp và cách tránh (VD: nạp lại dropdown khi ModelState hỏng).
- Nợ kỹ thuật cố ý để lại, ghi vào mục "Nợ kỹ thuật đã biết" kèm lý do — để phase sau không "sửa" nửa vời.

Chỉ ghi thứ **thay đổi cách viết code**. Không chép lại giải thích lý thuyết, không biến `CLAUDE.md` thành giáo trình. Nếu phase vừa rồi không phát sinh quy ước nào thật sự mới, nói rõ "không có gì cần thêm" thay vì thêm cho có.

**7b. Commit.** Soạn commit message ngắn gọn theo chuẩn Conventional Commits (`feat:`, `fix:`, `refactor:`...) kèm 1-2 câu tóm tắt thay đổi để dùng cho PR description. Nếu working tree lẫn nhiều thay đổi khác bản chất (VD: refactor + tính năng mới), đề xuất tách thành nhiều commit.

**Chỉ soạn sẵn message — hỏi người dùng trước khi thực sự chạy `git commit`.**

---

## Ghi chú khi áp dụng
- Người dùng nói "làm nhanh, bỏ qua giải thích" cho task cụ thể → tôn trọng, chỉ chạy **Bước 1, 3, 6, 7**.
- Một Phase có nhiều tính năng nhỏ lặp lại pattern đã học → chạy vòng lặp rút gọn (bỏ Bước 1, 2, 5), nhưng **luôn giữ đủ 7 bước cho phần đầu tiên của mỗi khái niệm mới**.
- Bước 7a (cập nhật `CLAUDE.md`) **không được bỏ kể cả khi chạy vòng rút gọn** — đây chính là cơ chế giữ cho các phase sau nhất quán với phase trước.
