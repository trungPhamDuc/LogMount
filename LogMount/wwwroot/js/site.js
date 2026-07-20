// Cumulative multi-file upload: each file picker selection adds to the list
// instead of replacing the previous selection.
(function () {
    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    function formatFileSize(bytes) {
        if (bytes < 1024) {
            return bytes + ' B';
        }

        if (bytes < 1024 * 1024) {
            return (bytes / 1024).toFixed(1) + ' KB';
        }

        return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
    }

    function initCumulativeFileUpload(inputId, listId, formId) {
        const input = document.getElementById(inputId);
        const list = document.getElementById(listId);
        const form = document.getElementById(formId);

        if (!input || !list || !form) {
            return;
        }

        let accumulatedFiles = [];

        function fileKey(file) {
            return file.name + '|' + file.size + '|' + file.lastModified;
        }

        function syncInput() {
            const dataTransfer = new DataTransfer();
            accumulatedFiles.forEach(function (file) {
                dataTransfer.items.add(file);
            });
            input.files = dataTransfer.files;
        }

        function renderList() {
            list.innerHTML = '';

            if (accumulatedFiles.length === 0) {
                list.classList.add('d-none');
                return;
            }

            list.classList.remove('d-none');

            accumulatedFiles.forEach(function (file, index) {
                const item = document.createElement('li');
                item.className = 'list-group-item d-flex justify-content-between align-items-center py-2 px-0 border-0';
                item.innerHTML =
                    '<span class="text-truncate me-2">' +
                    escapeHtml(file.name) +
                    ' <span class="text-muted">(' + formatFileSize(file.size) + ')</span></span>' +
                    '<button type="button" class="btn btn-sm btn-outline-danger flex-shrink-0" data-index="' + index + '">Xóa</button>';
                list.appendChild(item);
            });

            list.querySelectorAll('button[data-index]').forEach(function (button) {
                button.addEventListener('click', function () {
                    const index = parseInt(button.getAttribute('data-index'), 10);
                    accumulatedFiles.splice(index, 1);
                    syncInput();
                    renderList();
                });
            });
        }

        input.addEventListener('change', function () {
            const existingKeys = new Set(accumulatedFiles.map(fileKey));

            Array.from(input.files).forEach(function (file) {
                const key = fileKey(file);
                if (!existingKeys.has(key)) {
                    accumulatedFiles.push(file);
                    existingKeys.add(key);
                }
            });

            syncInput();
            renderList();
            input.value = '';
        });

        form.addEventListener('submit', function (event) {
            const submitter = event.submitter;
            const formAction = submitter && submitter.getAttribute('formaction');

            if (formAction && formAction.toLowerCase().includes('clear')) {
                return;
            }

            syncInput();

            if (accumulatedFiles.length === 0) {
                event.preventDefault();
                window.alert('Vui lòng chọn ít nhất một file.');
            }
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        initCumulativeFileUpload('log-upload-input', 'log-upload-file-list', 'log-upload-form');
    });
})();
