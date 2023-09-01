#pragma once

#include <memory>
#include <mutex>
#include <vector>

template <typename T>
class CircularBuffer
{
public:
    explicit CircularBuffer(size_t size) :
        buf_(std::unique_ptr<T[]>(new T[size])),
        max_size_(size)
    {

    }

    void put(T item)
    {
        std::lock_guard<std::mutex> lock(mutex_);

        buf_[head_] = item;

        if (full_)
        {
            tail_ = (tail_ + 1) % max_size_;
        }

        head_ = (head_ + 1) % max_size_;

        full_ = head_ == tail_;
    }

    [[nodiscard]] bool empty() const
    {
        //if head and tail are equal, we are empty
        return (!full_ && (head_ == tail_));
    }

    [[nodiscard]] bool full() const
    {
        //If tail is ahead the head by 1, we are full
        return full_;
    }

    [[nodiscard]] size_t capacity() const
    {
        return max_size_;
    }

    [[nodiscard]] size_t size() const
    {
        size_t size = max_size_;

        if (!full_)
        {
            if (head_ >= tail_)
            {
                size = head_ - tail_;
            }
            else
            {
                size += head_ - tail_;
            }
        }

        return size;
    }

    // the implementation of this function is simplified by the fact that head_ will never be lower than tail_
    // when compared to the original implementation of this class
    std::vector<T> content()
    {
        std::lock_guard<std::mutex> lock(mutex_);

        return std::vector<T>(buf_.get(), buf_.get() + size());
    }

    T peak_back()
    {
        std::lock_guard<std::mutex> lock(mutex_);

        return empty() ? T() : buf_[tail_];
    }

private:
    std::mutex mutex_;
    std::unique_ptr<T[]> buf_;
    size_t head_ = 0;
    size_t tail_ = 0;
    const size_t max_size_;
    bool full_ = false;
};
